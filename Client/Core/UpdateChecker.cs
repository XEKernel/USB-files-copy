using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace U盘文件复制.Core
{
    /// <summary>
    /// GitHub Releases 自动更新检查（纯后端）
    /// </summary>
    public static class UpdateChecker
    {
        private const string ReleasesApiUrl = "https://api.github.com/repos/XEKernel/USB-files-copy/releases/latest";

        /// <summary>
        /// 查询最新 Release（tag 与下载页 URL），失败返回 null
        /// </summary>
        public static async Task<(string tag, string htmlUrl)?> GetLatestReleaseAsync()
        {
            try
            {
                using (var handler = new HttpClientHandler())
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) })
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "USB-Files-Copy-Client");
                    var json = await client.GetStringAsync(ReleasesApiUrl);
                    var doc = Newtonsoft.Json.Linq.JObject.Parse(json);
                    string tag = (string)doc["tag_name"];
                    string url = (string)doc["html_url"];
                    return (tag, url);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 判断远程版本是否比当前版本新（支持 v1.2.3 / 1.2.3.4 格式）
        /// </summary>
        public static bool IsNewerThanCurrent(string remoteTag, string currentVersion)
        {
            var remote = ParseVersion(remoteTag);
            var current = ParseVersion(currentVersion);
            if (remote == null || current == null)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (remote[i] > current[i]) return true;
                if (remote[i] < current[i]) return false;
            }
            return false;
        }

        private static int[] ParseVersion(string version)
        {
            try
            {
                var parts = (version ?? "").TrimStart('v', 'V').Split('.');
                var result = new int[4];
                for (int i = 0; i < 4; i++)
                    result[i] = i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
