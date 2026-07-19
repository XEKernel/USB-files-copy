using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace U盘文件复制.Server.Middleware
{
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string[] _allowedTokens;

        public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            // 启动时一次性读取令牌列表，避免每请求读取配置
            _allowedTokens = configuration.GetSection("FileStorage:AllowedTokens").Get<string[]>() ?? Array.Empty<string>();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 只保护 /api/ 路径，静态文件等其他请求全部放行
            if (!context.Request.Path.StartsWithSegments("/api/"))
            {
                await _next(context);
                return;
            }

            // 健康检查端点免认证
            if (context.Request.Path.StartsWithSegments("/api/health"))
            {
                await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue("Authorization", out StringValues authHeader))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("缺少 Authorization 头");
                return;
            }

            var authValue = authHeader.ToString();
            if (!authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Authorization 格式错误，应为 Bearer token");
                return;
            }

            var token = authValue.Substring("Bearer ".Length).Trim();
            if (string.IsNullOrWhiteSpace(token) || !_allowedTokens.Contains(token))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("无效的令牌");
                return;
            }

            await _next(context);
        }
    }
}