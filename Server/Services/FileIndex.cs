using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace U盘文件复制.Server.Services
{
    /// <summary>
    /// SQLite 文件索引：为搜索/统计提供索引查询，避免每次全盘扫描。
    /// 所有操作通过单连接 + 锁串行化，保证线程安全。
    /// </summary>
    public class FileIndex : IDisposable
    {
        private readonly string _dbPath;
        private readonly object _lock = new object();
        private SqliteConnection? _conn;
        private volatile bool _ready;

        /// <summary>索引是否已就绪（未就绪时调用方应回退文件系统扫描）</summary>
        public bool IsReady => _ready;

        public FileIndex(string dbPath)
        {
            _dbPath = dbPath;
        }

        /// <summary>
        /// 打开数据库并建表（幂等）
        /// </summary>
        public void Open()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            _conn = new SqliteConnection(cs);
            _conn.Open();

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS files (
                    path       TEXT PRIMARY KEY,
                    name       TEXT NOT NULL,
                    size       INTEGER NOT NULL,
                    last_write TEXT NOT NULL,
                    is_dir     INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_files_name ON files(name);
                CREATE INDEX IF NOT EXISTS idx_files_last_write ON files(last_write);
                """;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 全量重建索引（清空后扫描根目录）
        /// </summary>
        /// <param name="rootPath">存储根目录</param>
        /// <param name="isExcluded">排除目录判断（临时分块/回收站）</param>
        /// <param name="log">日志回调</param>
        public void Rebuild(string rootPath, Func<string, bool> isExcluded, Action<string, bool>? log = null)
        {
            try
            {
                lock (_lock)
                {
                    using (var clear = _conn!.CreateCommand())
                    {
                        clear.CommandText = "DELETE FROM files";
                        clear.ExecuteNonQuery();
                    }

                    var rootDir = new DirectoryInfo(rootPath);
                    if (!rootDir.Exists)
                    {
                        _ready = true;
                        return;
                    }

                    using var tx = _conn.BeginTransaction();
                    using var insert = _conn.CreateCommand();
                    insert.CommandText = """
                        INSERT OR REPLACE INTO files (path, name, size, last_write, is_dir)
                        VALUES (@path, @name, @size, @last_write, 0)
                        """;
                    var pPath = insert.Parameters.Add("@path", SqliteType.Text);
                    var pName = insert.Parameters.Add("@name", SqliteType.Text);
                    var pSize = insert.Parameters.Add("@size", SqliteType.Integer);
                    var pLast = insert.Parameters.Add("@last_write", SqliteType.Text);

                    int count = 0;
                    foreach (var file in rootDir.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        if (isExcluded(file.FullName))
                            continue;

                        var relPath = file.FullName.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                        pPath.Value = relPath;
                        pName.Value = file.Name;
                        pSize.Value = file.Length;
                        pLast.Value = file.LastWriteTimeUtc.ToString("O");
                        insert.ExecuteNonQuery();

                        if (++count % 1000 == 0)
                            log?.Invoke($"索引构建中… {count} 个文件", false);
                    }
                    tx.Commit();
                    log?.Invoke($"文件索引构建完成，共 {count} 个文件", false);
                }
                _ready = true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"文件索引构建失败：{ex.Message}", true);
                _ready = false;
            }
        }

        /// <summary>
        /// 增量更新：写入/更新单条记录
        /// </summary>
        public void Upsert(string relativePath, string name, long sizeBytes, DateTime lastWriteUtc)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return;

            try
            {
                lock (_lock)
                {
                    using var cmd = _conn!.CreateCommand();
                    cmd.CommandText = """
                        INSERT OR REPLACE INTO files (path, name, size, last_write, is_dir)
                        VALUES (@path, @name, @size, @last_write, 0)
                        """;
                    cmd.Parameters.AddWithValue("@path", relativePath);
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@size", sizeBytes);
                    cmd.Parameters.AddWithValue("@last_write", lastWriteUtc.ToString("O"));
                    cmd.ExecuteNonQuery();
                }
            }
            catch { /* 索引更新失败不影响文件操作 */ }
        }

        /// <summary>
        /// 增量更新：删除记录
        /// </summary>
        public void Remove(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return;

            try
            {
                lock (_lock)
                {
                    using var cmd = _conn!.CreateCommand();
                    cmd.CommandText = "DELETE FROM files WHERE path = @path";
                    cmd.Parameters.AddWithValue("@path", relativePath);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        /// <summary>
        /// 索引搜索（分页）。返回 null 表示索引不可用，调用方应回退扫描。
        /// </summary>
        public (int total, List<FileMetadata> items)? Search(
            string keyword, string extension,
            DateTime? startDate, DateTime? endDate,
            int page, int pageSize)
        {
            if (!_ready || _conn == null)
                return null;

            try
            {
                lock (_lock)
                {
                    var sb = new StringBuilder("SELECT path, name, size, last_write FROM files WHERE 1=1");
                    var pars = new List<SqliteParameter>();

                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        sb.Append(" AND (name LIKE @kw)");
                        pars.Add(new SqliteParameter("@kw", $"%{keyword}%"));
                    }
                    if (!string.IsNullOrWhiteSpace(extension))
                    {
                        var ext = extension.TrimStart('.').Trim();
                        sb.Append(" AND (name LIKE @ext)");
                        pars.Add(new SqliteParameter("@ext", $"%.{ext}"));
                    }
                    if (startDate.HasValue)
                    {
                        sb.Append(" AND last_write >= @start");
                        pars.Add(new SqliteParameter("@start", startDate.Value.ToString("O")));
                    }
                    if (endDate.HasValue)
                    {
                        sb.Append(" AND last_write <= @end");
                        pars.Add(new SqliteParameter("@end", endDate.Value.AddDays(1).ToString("O")));
                    }

                    // 提取 WHERE 条件部分，供总数与分页查询复用
                    var cond = sb.ToString();
                    var wherePart = cond.Substring(cond.IndexOf(" WHERE ", StringComparison.Ordinal) + 7);

                    // 总数
                    int total;
                    using (var totalCmd = _conn.CreateCommand())
                    {
                        totalCmd.CommandText = "SELECT COUNT(*) FROM files WHERE " + wherePart;
                        foreach (var p in pars)
                            totalCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        total = Convert.ToInt32(totalCmd.ExecuteScalar());
                    }

                    // 分页查询
                    var items = new List<FileMetadata>();
                    using (var qCmd = _conn.CreateCommand())
                    {
                        qCmd.CommandText = $"SELECT path, name, size, last_write FROM files WHERE {wherePart} ORDER BY last_write DESC LIMIT @limit OFFSET @offset";
                        foreach (var p in pars)
                            qCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                        qCmd.Parameters.AddWithValue("@limit", pageSize);
                        qCmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

                        using var reader = qCmd.ExecuteReader();
                        while (reader.Read())
                        {
                            items.Add(new FileMetadata
                            {
                                Path = reader.GetString(0),
                                Name = reader.GetString(1),
                                SizeBytes = reader.GetInt64(2),
                                LastWriteTimeUtc = DateTime.TryParse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.MinValue,
                                IsDirectory = false
                            });
                        }
                    }

                    return (total, items);
                }
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _conn?.Dispose();
                _conn = null;
            }
        }
    }
}
