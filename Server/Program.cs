using Microsoft.OpenApi.Models;
using U盘文件复制.Server.Middleware;
using U盘文件复制.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. 读取文件存储配置
var storageConfig = builder.Configuration.GetSection("FileStorage");
string rootPath = storageConfig["RootPath"] ?? "Storage";
string tempChunkFolder = storageConfig["TempChunkFolder"] ?? "_chunks";
long maxFileSizeBytes = storageConfig.GetValue<long>("MaxFileSizeBytes", 1073741824); // 1GB
// 单个分块上限（默认 16MB）；防止客户端不声明长度时分块灌满磁盘
long maxChunkSizeBytes = storageConfig.GetValue<long>("MaxChunkSizeBytes", 16 * 1024 * 1024);
// 受保护的系统文件名（索引库/审计日志），默认不出现在列表、不可下载或删除
var reservedFileNames = storageConfig.GetSection("ReservedNames").Get<string[]>();

var allowedTokens = (storageConfig.GetSection("AllowedTokens").Get<string[]>() ?? Array.Empty<string>())
    .Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
var allowedPasswords = (storageConfig.GetSection("BasicPasswords").Get<string[]>() ?? Array.Empty<string>())
    .Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

// 未配置任何凭证时明确失败：否则服务能启动但所有请求都会被拒绝，部署者难以定位
if (allowedTokens.Length == 0 && allowedPasswords.Length == 0)
{
    Console.Error.WriteLine(
        "[启动失败] 未配置任何访问凭证。" + Environment.NewLine +
        "请设置 appsettings.json 的 FileStorage:AllowedTokens（随机长字符串），" + Environment.NewLine +
        "或使用环境变量 FileStorage__AllowedTokens__0=<你的令牌>。" + Environment.NewLine +
        "生成方式：openssl rand -hex 24");
    Environment.Exit(1);
}

foreach (var token in allowedTokens.Where(t => t.Length < 16))
    Console.Error.WriteLine($"[警告] 存在过短的访问令牌（{token.Length} 字符），建议使用 openssl rand -hex 24 生成 48 位随机串。");

// 2. 注册单例服务
builder.Services.AddSingleton<IFileStore>(provider =>
    new LocalFileStore(rootPath, tempChunkFolder, maxFileSizeBytes, maxChunkSizeBytes, reservedFileNames));

// 3. 注册分块自动清理后台服务（每 IntervalHours 小时清理过期分块）
builder.Services.Configure<U盘文件复制.Server.Services.ChunkCleanupOptions>(
    builder.Configuration.GetSection("Cleanup"));
builder.Services.AddHostedService<U盘文件复制.Server.Services.ChunkCleanupService>();

// 3. 添加控制器
builder.Services.AddControllers();

// 4. 配置 CORS
// 说明：Web 管理面板与客户端都在同源/非浏览器场景下工作，默认不需要任何跨域授权。
// 因此默认「不允许跨域」（AllowedOrigins 为空），只有显式配置来源才放行；
// 配置 "*" 会允许任意网站读取响应（配合令牌泄露等于开放全部接口），请勿在生产使用。
var corsOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
bool allowAnyOrigin = corsOrigins.Any(o => o == "*");

if (allowAnyOrigin)
    Console.Error.WriteLine("[警告] Cors:AllowedOrigins 配置为 \"*\"，任何网站均可跨域调用本服务，生产环境请改为具体域名。");

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultPolicy", policy =>
    {
        if (allowAnyOrigin)
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins(corsOrigins);   // 空数组 = 不授权任何跨域来源

        policy.AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Length", "Content-Range", "Last-Modified");
    });
});

// 5. 配置请求体大小限制
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxFileSizeBytes;
});

// 6. Swagger（仅开发环境注册，减少生产环境开销）
builder.Services.AddEndpointsApiExplorer();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "U盘文件复制器 API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "请输入令牌: Bearer {your-token}",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });
}

var app = builder.Build();

// ===== 中间件管道（顺序很重要） =====

// 7. 异常处理：对外只返回通用错误，细节写入服务端日志
// （原实现把 ex.Message 直接回显给调用方，会泄露绝对路径、账户名等内部信息）
app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            var ex = feature?.Error;

            var (statusCode, message) = ex switch
            {
                UnauthorizedAccessException => (403, "无权访问该路径"),
                FileNotFoundException => (404, "文件不存在"),
                _ => (500, "服务器内部错误")
            };

            if (ex != null)
                app.Logger.LogError(ex, "未处理异常 {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(new { error = message }));
        });
    });

// 8. CORS（放在认证之前）
app.UseCors("DefaultPolicy");

// 9. 请求日志
app.UseMiddleware<RequestLoggingMiddleware>();

// 10. 启用默认文档 + 静态文件（支持 wwwroot 前端的 HTML/JS/CSS）
// UseDefaultFiles 将 / 重写为 /index.html，必须在 UseStaticFiles 之前
app.UseDefaultFiles();
app.UseStaticFiles();

// 11. Swagger（开发环境）
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 12. 令牌验证（放在授权之前，控制器之前）
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();
