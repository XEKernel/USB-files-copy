using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace U盘文件复制.Server.Middleware
{
    /// <summary>
    /// API 认证中间件，支持两种认证方式（任一通过即可）：
    /// 1. Bearer Token：Authorization: Bearer {token}，匹配 FileStorage:AllowedTokens
    /// 2. Basic 认证：Authorization: Basic base64("usercopy:{password}")，匹配 FileStorage:BasicPasswords
    /// 仅保护 /api/ 路径（/api/health 免认证）。
    /// 认证通过后写入访问审计日志（FileStorage:AuditLogPath）。
    /// </summary>
    public class ApiKeyAuthMiddleware
    {
        private static readonly object AuditLock = new object();

        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyAuthMiddleware> _logger;
        private readonly HashSet<string> _allowedTokens;
        private readonly HashSet<string> _allowedPasswords;
        private readonly string _auditLogPath;

        public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyAuthMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            // 启动时一次性读取令牌列表，避免每请求读取配置
            _allowedTokens = ToHashSet(configuration.GetSection("FileStorage:AllowedTokens").Get<string[]>());
            _allowedPasswords = ToHashSet(configuration.GetSection("FileStorage:BasicPasswords").Get<string[]>());
            _auditLogPath = configuration["FileStorage:AuditLogPath"] ?? Path.Combine("Storage", "audit.log");
        }

        private static HashSet<string> ToHashSet(string[]? values)
        {
            return new HashSet<string>(
                values ?? Array.Empty<string>(),
                StringComparer.Ordinal);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 注意：PathString.StartsWithSegments 在此环境对 /api/ 路径返回 false，
            // 必须用 Value.StartsWith 判断，否则认证会被完全绕过（安全漏洞）
            var rawPath = context.Request.Path.Value ?? string.Empty;

            // 只保护 /api/ 路径，静态文件等其他请求全部放行
            if (!rawPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // 健康检查端点免认证
            if (rawPath.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase))
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
                LogAudit(context, GetAuthInfo(authValue));
                await _next(context);
                return;
            }

            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("无效的令牌");
        }

        /// <summary>
        /// 写入访问审计日志（令牌脱敏，仅记录前 8 位；相对路径基于进程工作目录）
        /// </summary>
        private void LogAudit(HttpContext context, string authInfo)
        {
            try
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {authInfo} {ip} {context.Request.Method} {context.Request.Path}{context.Request.QueryString}";
                _logger.LogInformation("审计日志写入: {Line}", line);

                lock (AuditLock)
                {
                    var fullPath = Path.IsPathRooted(_auditLogPath)
                        ? _auditLogPath
                        : Path.Combine(Directory.GetCurrentDirectory(), _auditLogPath);

                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.AppendAllText(fullPath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("审计日志写入失败: {Message}", ex.Message);
            }
        }

        private static string GetAuthInfo(string authValue)
        {
            if (authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authValue.Substring("Bearer ".Length).Trim();
                var masked = token.Length <= 8 ? token : token.Substring(0, 8) + "***";
                return $"Bearer:{masked}";
            }
            if (authValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return "Basic";
            return "Unknown";
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
