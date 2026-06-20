using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace U盘文件复制
{
    public static class FileHashHelper
    {
        public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = await Task.Run(() => sha256.ComputeHash(stream), ct);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static async Task<string> ComputeMd5Async(string filePath, CancellationToken ct = default)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = await Task.Run(() => md5.ComputeHash(stream), ct);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static async Task<bool> AreFilesIdenticalAsync(string filePath1, string filePath2, CancellationToken ct = default)
        {
            var fi1 = new FileInfo(filePath1);
            var fi2 = new FileInfo(filePath2);
            if (fi1.Length != fi2.Length) return false;

            string hash1 = await ComputeSha256Async(filePath1, ct);
            string hash2 = await ComputeSha256Async(filePath2, ct);
            return hash1 == hash2;
        }
    }
}