using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace U盘文件复制.Core
{
    /// <summary>
    /// 文件哈希工具（纯后端）：用于内容级去重判断
    /// </summary>
    public static class FileHasher
    {
        public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
            {
                byte[] hash = await Task.Run(() => sha256.ComputeHash(stream), ct);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// 判断两个文件内容是否完全相同：先比大小，再比 SHA256
        /// </summary>
        public static async Task<bool> AreFilesIdenticalAsync(string filePath1, string filePath2, CancellationToken ct = default)
        {
            var fi1 = new FileInfo(filePath1);
            var fi2 = new FileInfo(filePath2);
            if (!fi1.Exists || !fi2.Exists)
                return false;
            if (fi1.Length != fi2.Length)
                return false;

            string hash1 = await ComputeSha256Async(filePath1, ct);
            string hash2 = await ComputeSha256Async(filePath2, ct);
            return hash1 == hash2;
        }
    }
}
