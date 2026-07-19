using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace U盘文件复制
{
    /// <summary>
    /// 远程服务器目录浏览窗口
    /// </summary>
    public class RemoteBrowserForm : Form
    {
        private readonly IFileDestination _destination;
        private string _currentPath = "";
        private int _currentPage = 1;
        private const int PageSize = 50;

        private Panel _topPanel;
        private Label _lblPath;
        private TextBox _txtPath;
        private Button _btnGo;
        private Button _btnRefresh;
        private Button _btnUp;
        private Label _lblStatus;

        private ListView _listView;
        private ColumnHeader _colName;
        private ColumnHeader _colSize;
        private ColumnHeader _colDate;
        private ColumnHeader _colPath;

        private Panel _bottomPanel;
        private Button _btnPrev;
        private Button _btnNext;
        private Label _lblPage;
        private Button _btnDownload;
        private Button _btnDelete;
        private Button _btnStats;

        private Label _lblSearch;
        private TextBox _txtSearch;
        private Button _btnSearch;
        private Button _btnClearSearch;

        private CancellationTokenSource _cts;
        private bool _isSearchMode = false;

        public RemoteBrowserForm(IFileDestination destination)
        {
            _destination = destination ?? throw new ArgumentNullException(nameof(destination));
            InitializeComponent();
            Text = $"远程目录浏览 - {destination.DestinationType}";
        }

        private void InitializeComponent()
        {
            this.Size = new Size(900, 650);
            this.MinimumSize = new Size(700, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Font = new Font("Microsoft YaHei", 9F);

            // 顶部导航面板
            _topPanel = new Panel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(10) };

            _lblPath = new Label { Text = "目录:", Location = new Point(10, 12), AutoSize = true };
            _txtPath = new TextBox { Location = new Point(55, 9), Width = 400, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            _btnGo = new Button { Text = "跳转", Location = new Point(460, 7), Width = 60 };
            _btnUp = new Button { Text = "上级", Location = new Point(525, 7), Width = 60 };
            _btnRefresh = new Button { Text = "刷新", Location = new Point(590, 7), Width = 60 };

            _lblSearch = new Label { Text = "搜索:", Location = new Point(10, 44), AutoSize = true };
            _txtSearch = new TextBox { Location = new Point(55, 41), Width = 250 };
            _btnSearch = new Button { Text = "搜索", Location = new Point(310, 39), Width = 60 };
            _btnClearSearch = new Button { Text = "清除", Location = new Point(375, 39), Width = 60 };
            _lblStatus = new Label { Text = "就绪", Location = new Point(450, 44), AutoSize = true, ForeColor = Color.Gray };

            _topPanel.Controls.AddRange(new Control[] {
                _lblPath, _txtPath, _btnGo, _btnUp, _btnRefresh,
                _lblSearch, _txtSearch, _btnSearch, _btnClearSearch, _lblStatus
            });

            // 文件列表
            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _colName = new ColumnHeader { Text = "名称", Width = 280 };
            _colSize = new ColumnHeader { Text = "大小", Width = 90 };
            _colDate = new ColumnHeader { Text = "修改时间", Width = 150 };
            _colPath = new ColumnHeader { Text = "路径", Width = 300 };
            _listView.Columns.AddRange(new[] { _colName, _colSize, _colDate, _colPath });
            _listView.DoubleClick += ListView_DoubleClick;

            // 底部面板
            _bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(10) };
            _btnPrev = new Button { Text = "上一页", Location = new Point(10, 8), Width = 70, Enabled = false };
            _btnNext = new Button { Text = "下一页", Location = new Point(85, 8), Width = 70, Enabled = false };
            _lblPage = new Label { Text = "", Location = new Point(165, 12), AutoSize = true };
            _btnDownload = new Button { Text = "下载选中", Location = new Point(500, 8), Width = 90, Anchor = AnchorStyles.Right };
            _btnDelete = new Button { Text = "删除选中", Location = new Point(595, 8), Width = 90, Anchor = AnchorStyles.Right };
            _btnStats = new Button { Text = "统计", Location = new Point(690, 8), Width = 80, Anchor = AnchorStyles.Right };

            _bottomPanel.Controls.AddRange(new Control[] {
                _btnPrev, _btnNext, _lblPage, _btnDownload, _btnDelete, _btnStats
            });

            // 事件绑定
            _btnGo.Click += (s, e) => { _currentPath = _txtPath.Text.Trim(); _currentPage = 1; _isSearchMode = false; _ = LoadDirectoryAsync(); };
            _btnUp.Click += (s, e) => NavigateUp();
            _btnRefresh.Click += (s, e) => _ = LoadDirectoryAsync();
            _btnSearch.Click += (s, e) => { _isSearchMode = true; _currentPage = 1; _ = LoadDirectoryAsync(); };
            _btnClearSearch.Click += (s, e) => { _txtSearch.Text = ""; _isSearchMode = false; _currentPage = 1; _ = LoadDirectoryAsync(); };
            _txtPath.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { _btnGo.PerformClick(); } };
            _txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { _btnSearch.PerformClick(); } };
            _btnPrev.Click += (s, e) => { _currentPage--; _ = LoadDirectoryAsync(); };
            _btnNext.Click += (s, e) => { _currentPage++; _ = LoadDirectoryAsync(); };
            _btnDownload.Click += (s, e) => _ = DownloadSelectedFileAsync();
            _btnDelete.Click += (s, e) => _ = DeleteSelectedFileAsync();
            _btnStats.Click += (s, e) => _ = ShowStatsAsync();
            this.FormClosing += (s, e) => { _cts?.Cancel(); _cts?.Dispose(); };
            this.Load += (s, e) => _ = LoadDirectoryAsync();

            this.Controls.Add(_listView);
            this.Controls.Add(_bottomPanel);
            this.Controls.Add(_topPanel);
        }

        private void NavigateUp()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;
            var parts = _currentPath.TrimEnd('/').Split('/');
            if (parts.Length <= 1)
                _currentPath = "";
            else
                _currentPath = string.Join("/", parts.Take(parts.Length - 1));
            _currentPage = 1;
            _isSearchMode = false;
            _ = LoadDirectoryAsync();
        }

        private async Task LoadDirectoryAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            SetControlsEnabled(false);
            _lblStatus.Text = "加载中...";
            _txtPath.Text = _currentPath;

            try
            {
                if (_isSearchMode && !string.IsNullOrWhiteSpace(_txtSearch.Text))
                {
                    // 搜索模式
                    var result = await _destination.SearchFilesAsync(
                        _txtSearch.Text.Trim(), "", null, null, true,
                        _currentPage, PageSize, ct);

                    _listView.Items.Clear();
                    foreach (var item in result.Items)
                    {
                        var lvi = new ListViewItem(item.Name);
                        lvi.SubItems.Add(FormatSize(item.SizeBytes));
                        lvi.SubItems.Add(item.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                        lvi.SubItems.Add(item.Path);
                        lvi.Tag = item;
                        _listView.Items.Add(lvi);
                    }

                    _lblPage.Text = $"第 {result.Page} 页 / 共 {Math.Ceiling((double)result.Total / result.PageSize)} 页 (共 {result.Total} 项)";
                    _btnPrev.Enabled = result.Page > 1;
                    _btnNext.Enabled = result.Page * result.PageSize < result.Total;
                    _lblStatus.Text = $"搜索完成: {result.Total} 项";
                }
                else
                {
                    // 目录浏览模式
                    var result = await _destination.ListFilesAsync(_currentPath, false, ct);

                    _listView.Items.Clear();

                    // 先列出目录
                    var dirs = result.Where(f => f.IsDirectory).ToList();
                    var files = result.Where(f => !f.IsDirectory).ToList();

                    foreach (var dir in dirs)
                    {
                        var lvi = new ListViewItem("📁 " + dir.Name);
                        lvi.SubItems.Add("");
                        lvi.SubItems.Add(dir.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                        var cleanPath = dir.Path.TrimEnd('/');
                        lvi.SubItems.Add(cleanPath);
                        lvi.Tag = dir;
                        lvi.BackColor = Color.FromArgb(240, 248, 255);
                        _listView.Items.Add(lvi);
                    }

                    foreach (var file in files)
                    {
                        var lvi = new ListViewItem("📄 " + file.Name);
                        lvi.SubItems.Add(FormatSize(file.SizeBytes));
                        lvi.SubItems.Add(file.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                        lvi.SubItems.Add(file.Path);
                        lvi.Tag = file;
                        _listView.Items.Add(lvi);
                    }

                    _lblPage.Text = $"共 {result.Count} 项 (目录: {dirs.Count}, 文件: {files.Count})";
                    _btnPrev.Enabled = false;
                    _btnNext.Enabled = false;
                    _lblStatus.Text = $"加载完成: {dirs.Count} 个目录, {files.Count} 个文件";
                }
            }
            catch (OperationCanceledException) { _lblStatus.Text = "已取消"; }
            catch (Exception ex) { _lblStatus.Text = $"错误: {ex.Message}"; }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        private async void ListView_DoubleClick(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            var tag = _listView.SelectedItems[0].Tag as FileMetadataInfo;
            if (tag == null) return;

            if (tag.IsDirectory)
            {
                _currentPath = tag.Path.TrimEnd('/');
                _currentPage = 1;
                _isSearchMode = false;
                await LoadDirectoryAsync();
            }
        }

        private async Task DownloadSelectedFileAsync()
        {
            if (_listView.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择一个文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var tag = _listView.SelectedItems[0].Tag as FileMetadataInfo;
            if (tag == null || tag.IsDirectory) return;

            using (var sfd = new SaveFileDialog())
            {
                sfd.FileName = tag.Name;
                sfd.Title = "保存下载文件";
                if (sfd.ShowDialog() != DialogResult.OK) return;

                _lblStatus.Text = "下载中...";
                try
                {
                    if (_destination is HttpFileDestination http)
                    {
                        await DownloadHttpFileAsync(tag.Path, sfd.FileName);
                    }
                    else
                    {
                        // 本地复制
                        var localDest = _destination as LocalFileDestination;
                        var fullPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "U盘文件备份", tag.Path);
                        if (File.Exists(fullPath))
                            File.Copy(fullPath, sfd.FileName, true);
                        else
                            throw new FileNotFoundException($"文件不存在: {fullPath}");
                    }
                    _lblStatus.Text = "下载完成";
                    MessageBox.Show("下载成功!", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    _lblStatus.Text = $"下载失败: {ex.Message}";
                    MessageBox.Show($"下载失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task DownloadHttpFileAsync(string remotePath, string localPath)
        {
            if (_destination is HttpFileDestination http)
            {
                await NetworkHelper.DownloadFileAsync(http.Config, remotePath, localPath, CancellationToken.None);
            }
            else
            {
                throw new InvalidOperationException("当前目标不支持 HTTP 下载");
            }
        }

        private async Task DeleteSelectedFileAsync()
        {
            if (_listView.SelectedItems.Count == 0) return;
            var tag = _listView.SelectedItems[0].Tag as FileMetadataInfo;
            if (tag == null) return;

            if (MessageBox.Show($"确定删除 \"{tag.Name}\" 吗？\n此操作不可撤销!", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                await _destination.DeleteFileAsync(tag.Path, CancellationToken.None);
                _lblStatus.Text = "删除成功";
                await LoadDirectoryAsync();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"删除失败: {ex.Message}";
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ShowStatsAsync()
        {
            try
            {
                _lblStatus.Text = "获取统计...";
                var stats = await _destination.GetStatsAsync(CancellationToken.None);
                var msg = $"文件总数: {stats.TotalFiles:N0}\n" +
                          $"已用空间: {stats.TotalSizeMB:N2} MB\n" +
                          $"可用空间: {stats.AvailableDiskBytes / (1024.0 * 1024.0):N2} MB\n" +
                          $"磁盘总量: {stats.TotalDiskBytes / (1024.0 * 1024.0):N2} MB\n" +
                          $"未完成分块: {stats.PendingChunks}";
                MessageBox.Show(msg, "存储统计", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _lblStatus.Text = "就绪";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"获取统计失败: {ex.Message}";
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            _btnGo.Enabled = enabled;
            _btnUp.Enabled = enabled;
            _btnRefresh.Enabled = enabled;
            _btnSearch.Enabled = enabled;
            _btnClearSearch.Enabled = enabled;
            _btnDownload.Enabled = enabled;
            _btnDelete.Enabled = enabled;
            _btnStats.Enabled = enabled;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes == 0) return "-";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}
