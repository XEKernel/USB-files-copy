using Microsoft.OpenApi.Models;
using U盘文件复制.Server.Middleware;
using U盘文件复制.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. 读取文件存储配置
var storageConfig = builder.Configuration.GetSection("FileStorage");
string rootPath = storageConfig["RootPath"] ?? "Storage";
string tempChunkFolder = storageConfig["TempChunkFolder"] ?? "_chunks";
long maxFileSizeBytes = storageConfig.GetValue<long>("MaxFileSizeBytes", 1073741824); // 1GB
var allowedTokens = storageConfig.GetSection("AllowedTokens").Get<string[]>() ?? Array.Empty<string>();

// 2. 注册单例服务
builder.Services.AddSingleton<IFileStore>(provider =>
    new LocalFileStore(rootPath, tempChunkFolder, maxFileSizeBytes));

// 3. 添加控制器
builder.Services.AddControllers();

// 4. 配置 CORS（允许客户端跨域访问）
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "*" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultPolicy", policy =>
    {
        if (corsOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(corsOrigins);
        }
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

// 6. 配置 Swagger（支持 Bearer 令牌输入）
builder.Services.AddEndpointsApiExplorer();
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

var app = builder.Build();

// ===== 中间件管道（顺序很重要） =====

// 7. 异常处理（生产环境返回 JSON 错误信息）
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            var errorMessage = feature?.Error?.Message ?? "服务器内部错误";
            await context.Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(new { error = errorMessage }));
        });
    });
}

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
