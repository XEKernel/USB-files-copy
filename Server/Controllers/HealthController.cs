using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace U盘文件复制.Server.Controllers
{
    /// <summary>
    /// 健康检查控制器（无需认证）
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private static readonly string Version =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        /// <summary>
        /// 健康检查端点
        /// </summary>
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                status = "ok",
                timestamp = DateTime.UtcNow,
                version = Version
            });
        }
    }
}
