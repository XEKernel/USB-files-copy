using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace U盘文件复制.Server.Middleware
{
    /// <summary>
    /// 请求日志中间件，记录请求方法、路径、状态码和耗时
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var sw = Stopwatch.StartNew();
            var method = context.Request.Method;
            var path = context.Request.Path + context.Request.QueryString;
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            await _next(context);

            sw.Stop();
            var statusCode = context.Response.StatusCode;
            var elapsed = sw.ElapsedMilliseconds;

            // 根据状态码选择日志级别
            var logLevel = statusCode switch
            {
                >= 500 => LogLevel.Error,
                >= 400 => LogLevel.Warning,
                _ => LogLevel.Information
            };

            _logger.Log(logLevel, "{ClientIp} {Method} {Path} => {StatusCode} ({ElapsedMs}ms)",
                clientIp, method, path, statusCode, elapsed);
        }
    }
}
