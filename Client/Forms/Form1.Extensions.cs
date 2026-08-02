using System.Collections.Generic;
using System.Linq;
using U盘文件复制.Core;

namespace U盘文件复制
{
    partial class Form1
    {
        /// <summary>
        /// 获取选中的文件扩展名（从界面控件读取）
        /// </summary>
        private IEnumerable<string> GetSelectedExtensions()
        {
            if (chkAllFiles.Checked) return new[] { FileCategories.AllFilesPattern };

            var extensions = new List<string>();
            AddOfficeExtensions(extensions);
            AddMediaExtensions(extensions);
            AddCompressedExtensions(extensions);
            AddCustomExtensions(extensions);
            AddAudioExtension(extensions);
            return extensions;
        }

        private void AddOfficeExtensions(List<string> extensions)
        {
            if (chkPpt.Checked) extensions.AddRange(FileCategories.PowerPointExtensions);
            if (chkWord.Checked) extensions.AddRange(FileCategories.WordExtensions);
            if (chkExcel.Checked) extensions.AddRange(FileCategories.ExcelExtensions);
            if (chkPdf.Checked) extensions.AddRange(FileCategories.PdfExtensions);
        }

        private void AddMediaExtensions(List<string> extensions)
        {
            if (chkImage.Checked) extensions.AddRange(FileCategories.ImageExtensions);
            if (chkVideo.Checked) extensions.AddRange(FileCategories.VideoExtensions);
        }

        private void AddCompressedExtensions(List<string> extensions)
        {
            if (chkCompressed.Checked) extensions.AddRange(FileCategories.CompressedExtensions);
        }

        private void AddCustomExtensions(List<string> extensions)
        {
            if (!chkCustomExt.Checked || string.IsNullOrWhiteSpace(txtCustomExtensions.Text)) return;

            extensions.AddRange(
                txtCustomExtensions.Text.Split(',')
                    .Select(ext => ext.Trim().TrimStart('.'))
                    .Where(ext => !string.IsNullOrWhiteSpace(ext))
                    .Select(ext => $"*.{ext}")
            );
        }

        private void AddAudioExtension(List<string> extensions)
        {
            if (chkAudio.Checked) extensions.AddRange(FileCategories.AudioExtensions);
        }

        /// <summary>
        /// 解析逗号分隔的关键词列表（去重、去空白）
        /// </summary>
        private static List<string> ParseKeywords(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
