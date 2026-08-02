using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U盘文件复制.Server.Middleware
{
    /// <summary>
    /// API 认证中间件，支持两种认证方式（任一通过即可）：
    /// 1. Bearer Token：Authorization: Bearer {token}，匹配 FileStorage:AllowedTokens
    /// 2. Basic 认证：Authorization: Basic base64("usercopy:{password}")，匹配 FileStorage:BasicPasswords
    /// 仅保护 /api/ 路径（/api/health 免认证）
    /// </summary>
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly HashSet<string> _allowedTokens;
        private readonly HashSet<string> _allowedPasswords;

        public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            // 启动时一次性读取令牌列表，避免每请求读取配置
            _allowedTokens = ToHashSet(configuration.GetSection("FileStorage:AllowedTokens").Get<string[]>());
            _allowedPasswords = ToHashSet(configuration.GetSection("FileStorage:BasicPasswords").Get<string[]>());
        }

        private static HashSet<string> ToHashSet(string[]? values)
        {
            return new HashSet<string>(
                values ?? Array.Empty<string>(),
                StringComparer.Ordinal);
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
            if (IsAuthorized(authValue))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("无效的令牌");
        }

        private bool IsAuthorized(string authValue)
        {
            if (string.IsNullOrWhiteSpace(authValue))
                return false;

            // Bearer Token 认证
            if (authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authValue.Substring("Bearer ".Length).Trim();
                return !string.IsNullOrWhiteSpace(token) && _allowedTokens.Contains(token);
            }

            // Basic 基本认证（客户端使用 usercopy:{password}）
            if (authValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var base64 = authValue.Substring("Basic ".Length).Trim();
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                    const string userPrefix = "usercopy:";
                    if (!decoded.StartsWith(userPrefix, StringComparison.Ordinal))
                        return false;
                    var password = decoded.Substring(userPrefix.Length);
                    return !string.IsNullOrWhiteSpace(password) && _allowedPasswords.Contains(password);
                }
                catch (FormatException)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
