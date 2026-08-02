namespace U盘文件复制
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.txtTargetDir = new System.Windows.Forms.TextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.chkAudio = new System.Windows.Forms.CheckBox();
            this.chkCompressed = new System.Windows.Forms.CheckBox();
            this.chkAllFiles = new System.Windows.Forms.CheckBox();
            this.txtCustomExtensions = new System.Windows.Forms.TextBox();
            this.chkCustomExt = new System.Windows.Forms.CheckBox();
            this.chkVideo = new System.Windows.Forms.CheckBox();
            this.chkImage = new System.Windows.Forms.CheckBox();
            this.chkPdf = new System.Windows.Forms.CheckBox();
            this.chkExcel = new System.Windows.Forms.CheckBox();
            this.chkWord = new System.Windows.Forms.CheckBox();
            this.chkPpt = new System.Windows.Forms.CheckBox();
            this.lblCount = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.txtLogView = new System.Windows.Forms.RichTextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.btnBrowseDir = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.btnHide = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.rdoReplaceNewer = new System.Windows.Forms.RadioButton();
            this.rdoKeepBoth = new System.Windows.Forms.RadioButton();
            this.rdoOverwrite = new System.Windows.Forms.RadioButton();
            this.rdoSkip = new System.Windows.Forms.RadioButton();
            this.label5 = new System.Windows.Forms.Label();
            this.txtMaxSizeMB = new System.Windows.Forms.TextBox();
            this.chkSizeLimit = new System.Windows.Forms.CheckBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.groupBox6 = new System.Windows.Forms.GroupBox();
            this.cmbSpeedLimit = new System.Windows.Forms.ComboBox();
            this.label9 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.numSpeedMinutes = new System.Windows.Forms.NumericUpDown();
            this.chkSpeedLimit = new System.Windows.Forms.CheckBox();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.txtFolderKeywords = new System.Windows.Forms.TextBox();
            this.label14 = new System.Windows.Forms.Label();
            this.chkFolderFilter = new System.Windows.Forms.CheckBox();
            this.txtFileNameKeywords = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.chkFileNameFilter = new System.Windows.Forms.CheckBox();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.panel1 = new System.Windows.Forms.Panel();
            this.groupBox9 = new System.Windows.Forms.GroupBox();
            this.chkAutoStartHidden = new System.Windows.Forms.CheckBox();
            this.chkAutoStart = new System.Windows.Forms.CheckBox();
            this.grpNotify = new System.Windows.Forms.GroupBox();
            this.chkNotify = new System.Windows.Forms.CheckBox();
            this.chkTrayIcon = new System.Windows.Forms.CheckBox();
            this.grpWhitelist = new System.Windows.Forms.GroupBox();
            this.label24 = new System.Windows.Forms.Label();
            this.txtWhitelist = new System.Windows.Forms.TextBox();
            this.chkWhitelist = new System.Windows.Forms.CheckBox();
            this.groupBox8 = new System.Windows.Forms.GroupBox();
            this.chkDepthLimit = new System.Windows.Forms.CheckBox();
            this.numMaxDepth = new System.Windows.Forms.NumericUpDown();
            this.label15 = new System.Windows.Forms.Label();
            this.chkDirectoryTree = new System.Windows.Forms.CheckBox();
            this.groupBox7 = new System.Windows.Forms.GroupBox();
            this.chkLogWindow = new System.Windows.Forms.CheckBox();
            this.chkLogToFile = new System.Windows.Forms.CheckBox();
            this.chkLogNeutral = new System.Windows.Forms.CheckBox();
            this.chkLogErrors = new System.Windows.Forms.CheckBox();
            this.chkLogSuccess = new System.Windows.Forms.CheckBox();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.txtReverseCopyFile = new System.Windows.Forms.TextBox();
            this.label13 = new System.Windows.Forms.Label();
            this.chkReverseCopy = new System.Windows.Forms.CheckBox();
            this.txtStopCopyFile = new System.Windows.Forms.TextBox();
            this.label12 = new System.Windows.Forms.Label();
            this.chkStopCopy = new System.Windows.Forms.CheckBox();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.grpServerConfig = new System.Windows.Forms.GroupBox();
            this.txtServerToken = new System.Windows.Forms.TextBox();
            this.label19 = new System.Windows.Forms.Label();
            this.txtServerPassword = new System.Windows.Forms.TextBox();
            this.label18 = new System.Windows.Forms.Label();
            this.txtServerAddress = new System.Windows.Forms.TextBox();
            this.label17 = new System.Windows.Forms.Label();
            this.groupBox10 = new System.Windows.Forms.GroupBox();
            this.rdoServerSave = new System.Windows.Forms.RadioButton();
            this.rdoLocalSave = new System.Windows.Forms.RadioButton();
            this.label16 = new System.Windows.Forms.Label();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.linkLabel2 = new System.Windows.Forms.LinkLabel();
            this.linkLabel1 = new System.Windows.Forms.LinkLabel();
            this.label11 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label20 = new System.Windows.Forms.Label();
            this.txtServerPort = new System.Windows.Forms.TextBox();
            this.btnTestConn = new System.Windows.Forms.Button();
            this.lblConnStatus = new System.Windows.Forms.Label();
            this.chkUseHttps = new System.Windows.Forms.CheckBox();
            this.chkChunkedUpload = new System.Windows.Forms.CheckBox();
            this.label22 = new System.Windows.Forms.Label();
            this.txtChunkSize = new System.Windows.Forms.TextBox();
            this.cmbChunkUnit = new System.Windows.Forms.ComboBox();
            this._btnBrowseRemote = new System.Windows.Forms.Button();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numSpeedMinutes)).BeginInit();
            this.groupBox4.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.panel1.SuspendLayout();
            this.groupBox9.SuspendLayout();
            this.groupBox8.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxDepth)).BeginInit();
            this.groupBox7.SuspendLayout();
            this.groupBox5.SuspendLayout();
            this.tabPage3.SuspendLayout();
            this.grpServerConfig.SuspendLayout();
            this.groupBox10.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.SuspendLayout();
            // 
            // txtTargetDir
            // 
            this.txtTargetDir.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtTargetDir.Location = new System.Drawing.Point(22, 100);
            this.txtTargetDir.Margin = new System.Windows.Forms.Padding(4);
            this.txtTargetDir.Name = "txtTargetDir";
            this.txtTargetDir.Size = new System.Drawing.Size(214, 23);
            this.txtTargetDir.TabIndex = 26;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.chkAudio);
            this.groupBox1.Controls.Add(this.chkCompressed);
            this.groupBox1.Controls.Add(this.chkAllFiles);
            this.groupBox1.Controls.Add(this.txtCustomExtensions);
            this.groupBox1.Controls.Add(this.chkCustomExt);
            this.groupBox1.Controls.Add(this.chkVideo);
            this.groupBox1.Controls.Add(this.chkImage);
            this.groupBox1.Controls.Add(this.chkPdf);
            this.groupBox1.Controls.Add(this.chkExcel);
            this.groupBox1.Controls.Add(this.chkWord);
            this.groupBox1.Controls.Add(this.chkPpt);
            this.groupBox1.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.groupBox1.Location = new System.Drawing.Point(306, 8);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(144, 218);
            this.groupBox1.TabIndex = 24;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "复制文件类型";
            // 
            // chkAudio
            // 
            this.chkAudio.AutoSize = true;
            this.chkAudio.Location = new System.Drawing.Point(7, 127);
            this.chkAudio.Name = "chkAudio";
            this.chkAudio.Size = new System.Drawing.Size(75, 21);
            this.chkAudio.TabIndex = 15;
            this.chkAudio.Text = "音频文件";
            this.chkAudio.UseVisualStyleBackColor = true;
            // 
            // chkCompressed
            // 
            this.chkCompressed.AutoSize = true;
            this.chkCompressed.Location = new System.Drawing.Point(7, 146);
            this.chkCompressed.Margin = new System.Windows.Forms.Padding(4);
            this.chkCompressed.Name = "chkCompressed";
            this.chkCompressed.Size = new System.Drawing.Size(63, 21);
            this.chkCompressed.TabIndex = 14;
            this.chkCompressed.Text = "压缩包";
            this.chkCompressed.UseVisualStyleBackColor = true;
            // 
            // chkAllFiles
            // 
            this.chkAllFiles.AutoSize = true;
            this.chkAllFiles.Location = new System.Drawing.Point(7, 189);
            this.chkAllFiles.Margin = new System.Windows.Forms.Padding(4);
            this.chkAllFiles.Name = "chkAllFiles";
            this.chkAllFiles.Size = new System.Drawing.Size(96, 21);
            this.chkAllFiles.TabIndex = 13;
            this.chkAllFiles.Text = "所有文件(*.*)";
            this.chkAllFiles.UseVisualStyleBackColor = true;
            // 
            // txtCustomExtensions
            // 
            this.txtCustomExtensions.Location = new System.Drawing.Point(102, 165);
            this.txtCustomExtensions.Margin = new System.Windows.Forms.Padding(4);
            this.txtCustomExtensions.Name = "txtCustomExtensions";
            this.txtCustomExtensions.Size = new System.Drawing.Size(31, 23);
            this.txtCustomExtensions.TabIndex = 12;
            // 
            // chkCustomExt
            // 
            this.chkCustomExt.AutoSize = true;
            this.chkCustomExt.Location = new System.Drawing.Point(7, 167);
            this.chkCustomExt.Margin = new System.Windows.Forms.Padding(4);
            this.chkCustomExt.Name = "chkCustomExt";
            this.chkCustomExt.Size = new System.Drawing.Size(99, 21);
            this.chkCustomExt.TabIndex = 11;
            this.chkCustomExt.Text = "自定义扩展名";
            this.chkCustomExt.UseVisualStyleBackColor = true;
            // 
            // chkVideo
            // 
            this.chkVideo.AutoSize = true;
            this.chkVideo.Location = new System.Drawing.Point(7, 106);
            this.chkVideo.Margin = new System.Windows.Forms.Padding(4);
            this.chkVideo.Name = "chkVideo";
            this.chkVideo.Size = new System.Drawing.Size(75, 21);
            this.chkVideo.TabIndex = 7;
            this.chkVideo.Text = "视频文件";
            this.chkVideo.UseVisualStyleBackColor = true;
            // 
            // chkImage
            // 
            this.chkImage.AutoSize = true;
            this.chkImage.Location = new System.Drawing.Point(7, 87);
            this.chkImage.Margin = new System.Windows.Forms.Padding(4);
            this.chkImage.Name = "chkImage";
            this.chkImage.Size = new System.Drawing.Size(75, 21);
            this.chkImage.TabIndex = 6;
            this.chkImage.Text = "图片文件";
            this.chkImage.UseVisualStyleBackColor = true;
            // 
            // chkPdf
            // 
            this.chkPdf.AutoSize = true;
            this.chkPdf.Location = new System.Drawing.Point(7, 67);
            this.chkPdf.Margin = new System.Windows.Forms.Padding(4);
            this.chkPdf.Name = "chkPdf";
            this.chkPdf.Size = new System.Drawing.Size(73, 21);
            this.chkPdf.TabIndex = 3;
            this.chkPdf.Text = "PDF文件";
            this.chkPdf.UseVisualStyleBackColor = true;
            // 
            // chkExcel
            // 
            this.chkExcel.AutoSize = true;
            this.chkExcel.Location = new System.Drawing.Point(7, 49);
            this.chkExcel.Margin = new System.Windows.Forms.Padding(4);
            this.chkExcel.Name = "chkExcel";
            this.chkExcel.Size = new System.Drawing.Size(75, 21);
            this.chkExcel.TabIndex = 2;
            this.chkExcel.Text = "表格文件";
            this.chkExcel.UseVisualStyleBackColor = true;
            // 
            // chkWord
            // 
            this.chkWord.AutoSize = true;
            this.chkWord.Location = new System.Drawing.Point(7, 32);
            this.chkWord.Margin = new System.Windows.Forms.Padding(4);
            this.chkWord.Name = "chkWord";
            this.chkWord.Size = new System.Drawing.Size(75, 21);
            this.chkWord.TabIndex = 1;
            this.chkWord.Text = "文本文档";
            this.chkWord.UseVisualStyleBackColor = true;
            // 
            // chkPpt
            // 
            this.chkPpt.AutoSize = true;
            this.chkPpt.Location = new System.Drawing.Point(7, 15);
            this.chkPpt.Margin = new System.Windows.Forms.Padding(4);
            this.chkPpt.Name = "chkPpt";
            this.chkPpt.Size = new System.Drawing.Size(72, 21);
            this.chkPpt.TabIndex = 0;
            this.chkPpt.Text = "PPT文件\r\n";
            this.chkPpt.UseVisualStyleBackColor = true;
            // 
            // lblCount
            // 
            this.lblCount.AutoSize = true;
            this.lblCount.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblCount.Location = new System.Drawing.Point(5, 501);
            this.lblCount.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblCount.Name = "lblCount";
            this.lblCount.Size = new System.Drawing.Size(197, 17);
            this.lblCount.TabIndex = 31;
            this.lblCount.Text = "一共复制0文件，成功0个，失败0个";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("微软雅黑", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label1.Location = new System.Drawing.Point(3, 206);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(51, 20);
            this.label1.TabIndex = 30;
            this.label1.Text = "日志：";
            // 
            // txtLogView
            // 
            this.txtLogView.DetectUrls = false;
            this.txtLogView.Font = new System.Drawing.Font("微软雅黑", 7.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtLogView.Location = new System.Drawing.Point(7, 230);
            this.txtLogView.Margin = new System.Windows.Forms.Padding(4);
            this.txtLogView.Name = "txtLogView";
            this.txtLogView.Size = new System.Drawing.Size(443, 267);
            this.txtLogView.TabIndex = 29;
            this.txtLogView.Text = "";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label3.Location = new System.Drawing.Point(19, 79);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(104, 17);
            this.label3.TabIndex = 28;
            this.label3.Text = "选择本地保存目录";
            // 
            // btnBrowseDir
            // 
            this.btnBrowseDir.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnBrowseDir.Location = new System.Drawing.Point(239, 96);
            this.btnBrowseDir.Margin = new System.Windows.Forms.Padding(4);
            this.btnBrowseDir.Name = "btnBrowseDir";
            this.btnBrowseDir.Size = new System.Drawing.Size(59, 31);
            this.btnBrowseDir.TabIndex = 27;
            this.btnBrowseDir.Text = "浏览";
            this.btnBrowseDir.UseVisualStyleBackColor = true;
            this.btnBrowseDir.Click += new System.EventHandler(this.btnBrowseDir_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label2.Location = new System.Drawing.Point(18, 8);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(218, 63);
            this.label2.TabIndex = 25;
            this.label2.Text = "选择你需要复制文件的类型，\r\n并在本地选择一个目录来存\r\n储这些文件。";
            // 
            // btnHide
            // 
            this.btnHide.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnHide.Location = new System.Drawing.Point(94, 135);
            this.btnHide.Margin = new System.Windows.Forms.Padding(4);
            this.btnHide.Name = "btnHide";
            this.btnHide.Size = new System.Drawing.Size(108, 36);
            this.btnHide.TabIndex = 23;
            this.btnHide.Text = "后台运行";
            this.btnHide.UseVisualStyleBackColor = true;
            this.btnHide.Click += new System.EventHandler(this.btnHide_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.rdoReplaceNewer);
            this.groupBox2.Controls.Add(this.rdoKeepBoth);
            this.groupBox2.Controls.Add(this.rdoOverwrite);
            this.groupBox2.Controls.Add(this.rdoSkip);
            this.groupBox2.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.groupBox2.Location = new System.Drawing.Point(9, 57);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(268, 62);
            this.groupBox2.TabIndex = 32;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "重复文件设置";
            // 
            // rdoReplaceNewer
            // 
            this.rdoReplaceNewer.AutoSize = true;
            this.rdoReplaceNewer.Location = new System.Drawing.Point(186, 24);
            this.rdoReplaceNewer.Name = "rdoReplaceNewer";
            this.rdoReplaceNewer.Size = new System.Drawing.Size(74, 21);
            this.rdoReplaceNewer.TabIndex = 3;
            this.rdoReplaceNewer.Text = "以新换旧";
            this.rdoReplaceNewer.UseVisualStyleBackColor = true;
            // 
            // rdoKeepBoth
            // 
            this.rdoKeepBoth.AutoSize = true;
            this.rdoKeepBoth.Location = new System.Drawing.Point(118, 24);
            this.rdoKeepBoth.Name = "rdoKeepBoth";
            this.rdoKeepBoth.Size = new System.Drawing.Size(62, 21);
            this.rdoKeepBoth.TabIndex = 2;
            this.rdoKeepBoth.Text = "都保留";
            this.rdoKeepBoth.UseVisualStyleBackColor = true;
            // 
            // rdoOverwrite
            // 
            this.rdoOverwrite.AutoSize = true;
            this.rdoOverwrite.Location = new System.Drawing.Point(62, 24);
            this.rdoOverwrite.Name = "rdoOverwrite";
            this.rdoOverwrite.Size = new System.Drawing.Size(50, 21);
            this.rdoOverwrite.TabIndex = 1;
            this.rdoOverwrite.Text = "覆盖";
            this.rdoOverwrite.UseVisualStyleBackColor = true;
            // 
            // rdoSkip
            // 
            this.rdoSkip.AutoSize = true;
            this.rdoSkip.Checked = true;
            this.rdoSkip.Location = new System.Drawing.Point(6, 24);
            this.rdoSkip.Name = "rdoSkip";
            this.rdoSkip.Size = new System.Drawing.Size(50, 21);
            this.rdoSkip.TabIndex = 0;
            this.rdoSkip.TabStop = true;
            this.rdoSkip.Text = "跳过";
            this.rdoSkip.UseVisualStyleBackColor = true;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Font = new System.Drawing.Font("微软雅黑", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label5.Location = new System.Drawing.Point(6, 39);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(129, 20);
            this.label5.TabIndex = 33;
            this.label5.Text = "文件大小限制(MB):";
            // 
            // txtMaxSizeMB
            // 
            this.txtMaxSizeMB.Location = new System.Drawing.Point(135, 38);
            this.txtMaxSizeMB.Name = "txtMaxSizeMB";
            this.txtMaxSizeMB.Size = new System.Drawing.Size(66, 23);
            this.txtMaxSizeMB.TabIndex = 34;
            // 
            // chkSizeLimit
            // 
            this.chkSizeLimit.AutoSize = true;
            this.chkSizeLimit.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.chkSizeLimit.Location = new System.Drawing.Point(12, 17);
            this.chkSizeLimit.Name = "chkSizeLimit";
            this.chkSizeLimit.Size = new System.Drawing.Size(123, 21);
            this.chkSizeLimit.TabIndex = 35;
            this.chkSizeLimit.Text = "设置文件大小限制";
            this.chkSizeLimit.UseVisualStyleBackColor = true;
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.label5);
            this.groupBox3.Controls.Add(this.chkSizeLimit);
            this.groupBox3.Controls.Add(this.txtMaxSizeMB);
            this.groupBox3.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.groupBox3.Location = new System.Drawing.Point(9, 118);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(268, 65);
            this.groupBox3.TabIndex = 36;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "文件大小限制";
            // 
            // groupBox6
            // 
            this.groupBox6.Controls.Add(this.cmbSpeedLimit);
            this.groupBox6.Controls.Add(this.label9);
            this.groupBox6.Controls.Add(this.label8);
            this.groupBox6.Controls.Add(this.numSpeedMinutes);
            this.groupBox6.Controls.Add(this.chkSpeedLimit);
            this.groupBox6.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.groupBox6.Location = new System.Drawing.Point(8, 392);
            this.groupBox6.Name = "groupBox6";
            this.groupBox6.Size = new System.Drawing.Size(269, 85);
            this.groupBox6.TabIndex = 39;
            this.groupBox6.TabStop = false;
            this.groupBox6.Text = "复制速度控制";
            // 
            // cmbSpeedLimit
            // 
            this.cmbSpeedLimit.FormattingEnabled = true;
            this.cmbSpeedLimit.Items.AddRange(new object[] {
            "1 MB/秒",
            "2 MB/秒",
            "5 MB/秒",
            "10 MB/秒"});
            this.cmbSpeedLimit.Location = new System.Drawing.Point(165, 44);
            this.cmbSpeedLimit.Name = "cmbSpeedLimit";
            this.cmbSpeedLimit.Size = new System.Drawing.Size(83, 25);
            this.cmbSpeedLimit.TabIndex = 4;
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(102, 48);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(59, 17);
            this.label9.TabIndex = 3;
            this.label9.Text = "限速速度:";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(63, 49);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(32, 17);
            this.label8.TabIndex = 2;
            this.label8.Text = "分钟";
            // 
            // numSpeedMinutes
            // 
            this.numSpeedMinutes.Enabled = false;
            this.numSpeedMinutes.Location = new System.Drawing.Point(6, 46);
            this.numSpeedMinutes.Maximum = new decimal(new int[] {
            30,
            0,
            0,
            0});
            this.numSpeedMinutes.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numSpeedMinutes.Name = "numSpeedMinutes";
            this.numSpeedMinutes.Size = new System.Drawing.Size(56, 23);
            this.numSpeedMinutes.TabIndex = 1;
            this.numSpeedMinutes.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            // 
            // chkSpeedLimit
            // 
            this.chkSpeedLimit.AutoSize = true;
            this.chkSpeedLimit.Location = new System.Drawing.Point(6, 22);
            this.chkSpeedLimit.Name = "chkSpeedLimit";
            this.chkSpeedLimit.Size = new System.Drawing.Size(191, 21);
            this.chkSpeedLimit.TabIndex = 0;
            this.chkSpeedLimit.Text = "启用前X分钟限速（减少卡顿）";
            this.chkSpeedLimit.UseVisualStyleBackColor = true;
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.txtFolderKeywords);
            this.groupBox4.Controls.Add(this.label14);
            this.groupBox4.Controls.Add(this.chkFolderFilter);
            this.groupBox4.Controls.Add(this.txtFileNameKeywords);
            this.groupBox4.Controls.Add(this.label6);
            this.groupBox4.Controls.Add(this.chkFileNameFilter);
            this.groupBox4.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.groupBox4.Location = new System.Drawing.Point(9, 189);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(268, 190);
            this.groupBox4.TabIndex = 37;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "文件名过滤";
            // 
            // txtFolderKeywords
            // 
            this.txtFolderKeywords.Location = new System.Drawing.Point(9, 159);
            this.txtFolderKeywords.Multiline = true;
            this.txtFolderKeywords.Name = "txtFolderKeywords";
            this.txtFolderKeywords.Size = new System.Drawing.Size(248, 21);
            this.txtFolderKeywords.TabIndex = 5;
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(6, 126);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(188, 34);
            this.label14.TabIndex = 4;
            this.label14.Text = "输入复制时的文件夹中包含的文字\r\n(用英文逗号隔开):";
            // 
            // chkFolderFilter
            // 
            this.chkFolderFilter.AutoSize = true;
            this.chkFolderFilter.Location = new System.Drawing.Point(9, 104);
            this.chkFolderFilter.Name = "chkFolderFilter";
            this.chkFolderFilter.Size = new System.Drawing.Size(111, 21);
            this.chkFolderFilter.TabIndex = 3;
            this.chkFolderFilter.Text = "打开文件夹过滤";
            this.chkFolderFilter.UseVisualStyleBackColor = true;
            // 
            // txtFileNameKeywords
            // 
            this.txtFileNameKeywords.Location = new System.Drawing.Point(9, 77);
            this.txtFileNameKeywords.Multiline = true;
            this.txtFileNameKeywords.Name = "txtFileNameKeywords";
            this.txtFileNameKeywords.Size = new System.Drawing.Size(248, 21);
            this.txtFileNameKeywords.TabIndex = 2;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(6, 36);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(188, 34);
            this.label6.TabIndex = 1;
            this.label6.Text = "输入复制时的文件名中包含的文字\r\n(用英文逗号隔开):";
            // 
            // chkFileNameFilter
            // 
            this.chkFileNameFilter.AutoSize = true;
            this.chkFileNameFilter.Location = new System.Drawing.Point(9, 17);
            this.chkFileNameFilter.Name = "chkFileNameFilter";
            this.chkFileNameFilter.Size = new System.Drawing.Size(111, 21);
            this.chkFileNameFilter.TabIndex = 0;
            this.chkFileNameFilter.Text = "打开文件名过滤";
            this.chkFileNameFilter.UseVisualStyleBackColor = true;
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage3);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.tabControl1.Location = new System.Drawing.Point(3, 2);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(775, 551);
            this.tabControl1.TabIndex = 40;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.panel1);
            this.tabPage1.Controls.Add(this.txtLogView);
            this.tabPage1.Controls.Add(this.btnHide);
            this.tabPage1.Controls.Add(this.label2);
            this.tabPage1.Controls.Add(this.btnBrowseDir);
            this.tabPage1.Controls.Add(this.label3);
            this.tabPage1.Controls.Add(this.txtTargetDir);
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Controls.Add(this.groupBox1);
            this.tabPage1.Controls.Add(this.lblCount);
            this.tabPage1.Location = new System.Drawing.Point(4, 26);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(767, 521);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "主界面";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            this.panel1.AutoScroll = true;
            this.panel1.Controls.Add(this.grpWhitelist);
            this.panel1.Controls.Add(this.grpNotify);
            this.panel1.Controls.Add(this.groupBox9);
            this.panel1.Controls.Add(this.groupBox8);
            this.panel1.Controls.Add(this.groupBox7);
            this.panel1.Controls.Add(this.groupBox5);
            this.panel1.Controls.Add(this.groupBox6);
            this.panel1.Controls.Add(this.groupBox2);
            this.panel1.Controls.Add(this.groupBox3);
            this.panel1.Controls.Add(this.groupBox4);
            this.panel1.Location = new System.Drawing.Point(457, 8);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(304, 507);
            this.panel1.TabIndex = 41;
            // 
            // groupBox9
            // 
            this.groupBox9.Controls.Add(this.chkAutoStartHidden);
            this.groupBox9.Controls.Add(this.chkAutoStart);
            this.groupBox9.Location = new System.Drawing.Point(10, 15);
            this.groupBox9.Name = "groupBox9";
            this.groupBox9.Size = new System.Drawing.Size(267, 58);
            this.groupBox9.TabIndex = 43;
            this.groupBox9.TabStop = false;
            this.groupBox9.Text = "开机自启动";
            // 
            // chkAutoStartHidden
            // 
            this.chkAutoStartHidden.AutoSize = true;
            this.chkAutoStartHidden.Location = new System.Drawing.Point(4, 36);
            this.chkAutoStartHidden.Name = "chkAutoStartHidden";
            this.chkAutoStartHidden.Size = new System.Drawing.Size(270, 21);
            this.chkAutoStartHidden.TabIndex = 1;
            this.chkAutoStartHidden.Text = "开机启动时自动隐藏窗口,使用配置文件的设置";
            this.chkAutoStartHidden.UseVisualStyleBackColor = true;
            // 
            // chkAutoStart
            // 
            this.chkAutoStart.AutoSize = true;
            this.chkAutoStart.Location = new System.Drawing.Point(4, 15);
            this.chkAutoStart.Name = "chkAutoStart";
            this.chkAutoStart.Size = new System.Drawing.Size(87, 21);
            this.chkAutoStart.TabIndex = 0;
            this.chkAutoStart.Text = "开机自启动";
            this.chkAutoStart.UseVisualStyleBackColor = true;
            // 
            // grpNotify
            // 
            this.grpNotify.Controls.Add(this.chkNotify);
            this.grpNotify.Controls.Add(this.chkTrayIcon);
            this.grpNotify.Location = new System.Drawing.Point(8, 806);
            this.grpNotify.Name = "grpNotify";
            this.grpNotify.Size = new System.Drawing.Size(269, 89);
            this.grpNotify.TabIndex = 44;
            this.grpNotify.TabStop = false;
            this.grpNotify.Text = "通知与托盘";
            // 
            // chkNotify
            // 
            this.chkNotify.AutoSize = true;
            this.chkNotify.Location = new System.Drawing.Point(6, 52);
            this.chkNotify.Name = "chkNotify";
            this.chkNotify.Size = new System.Drawing.Size(144, 21);
            this.chkNotify.TabIndex = 1;
            this.chkNotify.Text = "复制完成时弹通知";
            this.chkNotify.UseVisualStyleBackColor = true;
            // 
            // chkTrayIcon
            // 
            this.chkTrayIcon.AutoSize = true;
            this.chkTrayIcon.Location = new System.Drawing.Point(6, 22);
            this.chkTrayIcon.Name = "chkTrayIcon";
            this.chkTrayIcon.Size = new System.Drawing.Size(135, 21);
            this.chkTrayIcon.TabIndex = 0;
            this.chkTrayIcon.Text = "显示系统托盘图标";
            this.chkTrayIcon.UseVisualStyleBackColor = true;
            // 
            // grpWhitelist
            // 
            this.grpWhitelist.Controls.Add(this.label24);
            this.grpWhitelist.Controls.Add(this.txtWhitelist);
            this.grpWhitelist.Controls.Add(this.chkWhitelist);
            this.grpWhitelist.Location = new System.Drawing.Point(8, 900);
            this.grpWhitelist.Name = "grpWhitelist";
            this.grpWhitelist.Size = new System.Drawing.Size(269, 100);
            this.grpWhitelist.TabIndex = 45;
            this.grpWhitelist.TabStop = false;
            this.grpWhitelist.Text = "U盘白名单";
            // 
            // label24
            // 
            this.label24.AutoSize = true;
            this.label24.Location = new System.Drawing.Point(6, 78);
            this.label24.Name = "label24";
            this.label24.Size = new System.Drawing.Size(215, 17);
            this.label24.TabIndex = 2;
            this.label24.Text = "填入U盘卷序列号，多个用逗号分隔";
            // 
            // txtWhitelist
            // 
            this.txtWhitelist.Location = new System.Drawing.Point(6, 52);
            this.txtWhitelist.Name = "txtWhitelist";
            this.txtWhitelist.Size = new System.Drawing.Size(257, 23);
            this.txtWhitelist.TabIndex = 1;
            // 
            // chkWhitelist
            // 
            this.chkWhitelist.AutoSize = true;
            this.chkWhitelist.Location = new System.Drawing.Point(6, 22);
            this.chkWhitelist.Name = "chkWhitelist";
            this.chkWhitelist.Size = new System.Drawing.Size(147, 21);
            this.chkWhitelist.TabIndex = 0;
            this.chkWhitelist.Text = "仅复制白名单中的U盘";
            this.chkWhitelist.UseVisualStyleBackColor = true;
            // 
            // groupBox8
            // 
            this.groupBox8.Controls.Add(this.chkDepthLimit);
            this.groupBox8.Controls.Add(this.numMaxDepth);
            this.groupBox8.Controls.Add(this.label15);
            this.groupBox8.Controls.Add(this.chkDirectoryTree);
            this.groupBox8.Location = new System.Drawing.Point(8, 712);
            this.groupBox8.Name = "groupBox8";
            this.groupBox8.Size = new System.Drawing.Size(269, 89);
            this.groupBox8.TabIndex = 42;
            this.groupBox8.TabStop = false;
            this.groupBox8.Text = "文件目录深度设置";
            // 
            // chkDepthLimit
            // 
            this.chkDepthLimit.AutoSize = true;
            this.chkDepthLimit.Location = new System.Drawing.Point(6, 43);
            this.chkDepthLimit.Name = "chkDepthLimit";
            this.chkDepthLimit.Size = new System.Drawing.Size(135, 21);
            this.chkDepthLimit.TabIndex = 3;
            this.chkDepthLimit.Text = "自定义复制文件深度";
            this.chkDepthLimit.UseVisualStyleBackColor = true;
            // 
            // numMaxDepth
            // 
            this.numMaxDepth.Location = new System.Drawing.Point(160, 62);
            this.numMaxDepth.Name = "numMaxDepth";
            this.numMaxDepth.Size = new System.Drawing.Size(42, 23);
            this.numMaxDepth.TabIndex = 2;
            // 
            // label15
            // 
            this.label15.AutoSize = true;
            this.label15.Location = new System.Drawing.Point(3, 64);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(164, 17);
            this.label15.TabIndex = 1;
            this.label15.Text = "设置复制文件时的目录深度：";
            // 
            // chkDirectoryTree
            // 
            this.chkDirectoryTree.AutoSize = true;
            this.chkDirectoryTree.Location = new System.Drawing.Point(6, 22);
            this.chkDirectoryTree.Name = "chkDirectoryTree";
            this.chkDirectoryTree.Size = new System.Drawing.Size(111, 21);
            this.chkDirectoryTree.TabIndex = 0;
            this.chkDirectoryTree.Text = "创建文件树状图";
            this.chkDirectoryTree.UseVisualStyleBackColor = true;
            // 
            // groupBox7
            // 
            this.groupBox7.Controls.Add(this.chkLogWindow);
            this.groupBox7.Controls.Add(this.chkLogToFile);
            this.groupBox7.Controls.Add(this.chkLogNeutral);
            this.groupBox7.Controls.Add(this.chkLogErrors);
            this.groupBox7.Controls.Add(this.chkLogSuccess);
            this.groupBox7.Location = new System.Drawing.Point(8, 606);
            this.groupBox7.Name = "groupBox7";
            this.groupBox7.Size = new System.Drawing.Size(269, 100);
            this.groupBox7.TabIndex = 42;
            this.groupBox7.TabStop = false;
            this.groupBox7.Text = "日志设置";
            // 
            // chkLogWindow
            // 
            this.chkLogWindow.AutoSize = true;
            this.chkLogWindow.Location = new System.Drawing.Point(6, 73);
            this.chkLogWindow.Name = "chkLogWindow";
            this.chkLogWindow.Size = new System.Drawing.Size(135, 21);
            this.chkLogWindow.TabIndex = 4;
            this.chkLogWindow.Text = "在当前窗口显示日志";
            this.chkLogWindow.UseVisualStyleBackColor = true;
            // 
            // chkLogToFile
            // 
            this.chkLogToFile.AutoSize = true;
            this.chkLogToFile.Location = new System.Drawing.Point(111, 49);
            this.chkLogToFile.Name = "chkLogToFile";
            this.chkLogToFile.Size = new System.Drawing.Size(123, 21);
            this.chkLogToFile.TabIndex = 3;
            this.chkLogToFile.Text = "将日志保存为文件";
            this.chkLogToFile.UseVisualStyleBackColor = true;
            // 
            // chkLogNeutral
            // 
            this.chkLogNeutral.AutoSize = true;
            this.chkLogNeutral.Location = new System.Drawing.Point(6, 49);
            this.chkLogNeutral.Name = "chkLogNeutral";
            this.chkLogNeutral.Size = new System.Drawing.Size(99, 21);
            this.chkLogNeutral.TabIndex = 2;
            this.chkLogNeutral.Text = "保留中性信息";
            this.chkLogNeutral.UseVisualStyleBackColor = true;
            // 
            // chkLogErrors
            // 
            this.chkLogErrors.AutoSize = true;
            this.chkLogErrors.Location = new System.Drawing.Point(111, 22);
            this.chkLogErrors.Name = "chkLogErrors";
            this.chkLogErrors.Size = new System.Drawing.Size(99, 21);
            this.chkLogErrors.TabIndex = 1;
            this.chkLogErrors.Text = "保留错误信息";
            this.chkLogErrors.UseVisualStyleBackColor = true;
            // 
            // chkLogSuccess
            // 
            this.chkLogSuccess.AutoSize = true;
            this.chkLogSuccess.Location = new System.Drawing.Point(6, 22);
            this.chkLogSuccess.Name = "chkLogSuccess";
            this.chkLogSuccess.Size = new System.Drawing.Size(99, 21);
            this.chkLogSuccess.TabIndex = 0;
            this.chkLogSuccess.Text = "保留成功信息";
            this.chkLogSuccess.UseVisualStyleBackColor = true;
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.txtReverseCopyFile);
            this.groupBox5.Controls.Add(this.label13);
            this.groupBox5.Controls.Add(this.chkReverseCopy);
            this.groupBox5.Controls.Add(this.txtStopCopyFile);
            this.groupBox5.Controls.Add(this.label12);
            this.groupBox5.Controls.Add(this.chkStopCopy);
            this.groupBox5.Location = new System.Drawing.Point(8, 483);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(269, 120);
            this.groupBox5.TabIndex = 40;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "U盘相关设置";
            // 
            // txtReverseCopyFile
            // 
            this.txtReverseCopyFile.Location = new System.Drawing.Point(89, 87);
            this.txtReverseCopyFile.Name = "txtReverseCopyFile";
            this.txtReverseCopyFile.Size = new System.Drawing.Size(100, 23);
            this.txtReverseCopyFile.TabIndex = 5;
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(9, 90);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(80, 17);
            this.label13.TabIndex = 4;
            this.label13.Text = "设置文件名：";
            // 
            // chkReverseCopy
            // 
            this.chkReverseCopy.AutoSize = true;
            this.chkReverseCopy.Location = new System.Drawing.Point(6, 66);
            this.chkReverseCopy.Name = "chkReverseCopy";
            this.chkReverseCopy.Size = new System.Drawing.Size(192, 21);
            this.chkReverseCopy.TabIndex = 3;
            this.chkReverseCopy.Text = "当U盘中有指定文件时反向复制";
            this.chkReverseCopy.UseVisualStyleBackColor = true;
            // 
            // txtStopCopyFile
            // 
            this.txtStopCopyFile.Location = new System.Drawing.Point(89, 43);
            this.txtStopCopyFile.Name = "txtStopCopyFile";
            this.txtStopCopyFile.Size = new System.Drawing.Size(100, 23);
            this.txtStopCopyFile.TabIndex = 2;
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(9, 46);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(80, 17);
            this.label12.TabIndex = 1;
            this.label12.Text = "设置文件名：";
            // 
            // chkStopCopy
            // 
            this.chkStopCopy.AutoSize = true;
            this.chkStopCopy.Location = new System.Drawing.Point(6, 22);
            this.chkStopCopy.Name = "chkStopCopy";
            this.chkStopCopy.Size = new System.Drawing.Size(192, 21);
            this.chkStopCopy.TabIndex = 0;
            this.chkStopCopy.Text = "当U盘中有指定文件时停止复制";
            this.chkStopCopy.UseVisualStyleBackColor = true;
            // 
            // tabPage3
            // 
            this.tabPage3.Controls.Add(this.grpServerConfig);
            this.tabPage3.Controls.Add(this.groupBox10);
            this.tabPage3.Controls.Add(this.label16);
            this.tabPage3.Location = new System.Drawing.Point(4, 26);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Size = new System.Drawing.Size(767, 521);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.Text = "服务器配置";
            this.tabPage3.UseVisualStyleBackColor = true;
            // 
            // grpServerConfig
            // 
            this.grpServerConfig.Controls.Add(this.cmbChunkUnit);
            this.grpServerConfig.Controls.Add(this.txtChunkSize);
            this.grpServerConfig.Controls.Add(this.label22);
            this.grpServerConfig.Controls.Add(this.chkChunkedUpload);
            this.grpServerConfig.Controls.Add(this.chkUseHttps);
            this.grpServerConfig.Controls.Add(this.lblConnStatus);
            this.grpServerConfig.Controls.Add(this.btnTestConn);
            // 
            // _btnBrowseRemote
            // 
            this._btnBrowseRemote.Location = new System.Drawing.Point(87, 143);
            this._btnBrowseRemote.Name = "_btnBrowseRemote";
            this._btnBrowseRemote.Size = new System.Drawing.Size(80, 23);
            this._btnBrowseRemote.TabIndex = 15;
            this._btnBrowseRemote.Text = "浏览远程";
            this._btnBrowseRemote.UseVisualStyleBackColor = true;
            this.grpServerConfig.Controls.Add(this._btnBrowseRemote);
            this.grpServerConfig.Controls.Add(this.txtServerPort);
            this.grpServerConfig.Controls.Add(this.label20);
            this.grpServerConfig.Controls.Add(this.txtServerToken);
            this.grpServerConfig.Controls.Add(this.label19);
            this.grpServerConfig.Controls.Add(this.txtServerPassword);
            this.grpServerConfig.Controls.Add(this.label18);
            this.grpServerConfig.Controls.Add(this.txtServerAddress);
            this.grpServerConfig.Controls.Add(this.label17);
            this.grpServerConfig.Location = new System.Drawing.Point(9, 106);
            this.grpServerConfig.Name = "grpServerConfig";
            this.grpServerConfig.Size = new System.Drawing.Size(240, 256);
            this.grpServerConfig.TabIndex = 3;
            this.grpServerConfig.TabStop = false;
            this.grpServerConfig.Text = "服务器配置";
            // 
            // txtServerToken
            // 
            this.txtServerToken.Location = new System.Drawing.Point(67, 80);
            this.txtServerToken.Name = "txtServerToken";
            this.txtServerToken.Size = new System.Drawing.Size(167, 23);
            this.txtServerToken.TabIndex = 5;
            // 
            // label19
            // 
            this.label19.AutoSize = true;
            this.label19.Location = new System.Drawing.Point(24, 86);
            this.label19.Name = "label19";
            this.label19.Size = new System.Drawing.Size(32, 17);
            this.label19.TabIndex = 4;
            this.label19.Text = "令牌";
            // 
            // txtServerPassword
            // 
            this.txtServerPassword.Location = new System.Drawing.Point(67, 51);
            this.txtServerPassword.Name = "txtServerPassword";
            this.txtServerPassword.Size = new System.Drawing.Size(167, 23);
            this.txtServerPassword.TabIndex = 3;
            // 
            // label18
            // 
            this.label18.AutoSize = true;
            this.label18.Location = new System.Drawing.Point(24, 54);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(32, 17);
            this.label18.TabIndex = 2;
            this.label18.Text = "密码";
            // 
            // txtServerAddress
            // 
            this.txtServerAddress.Location = new System.Drawing.Point(67, 22);
            this.txtServerAddress.Name = "txtServerAddress";
            this.txtServerAddress.Size = new System.Drawing.Size(167, 23);
            this.txtServerAddress.TabIndex = 1;
            // 
            // label17
            // 
            this.label17.AutoSize = true;
            this.label17.Location = new System.Drawing.Point(6, 25);
            this.label17.Name = "label17";
            this.label17.Size = new System.Drawing.Size(55, 17);
            this.label17.TabIndex = 0;
            this.label17.Text = "服务器IP";
            // 
            // groupBox10
            // 
            this.groupBox10.Controls.Add(this.rdoServerSave);
            this.groupBox10.Controls.Add(this.rdoLocalSave);
            this.groupBox10.Location = new System.Drawing.Point(9, 34);
            this.groupBox10.Name = "groupBox10";
            this.groupBox10.Size = new System.Drawing.Size(240, 66);
            this.groupBox10.TabIndex = 2;
            this.groupBox10.TabStop = false;
            this.groupBox10.Text = "文件保存位置";
            // 
            // rdoServerSave
            // 
            this.rdoServerSave.AutoSize = true;
            this.rdoServerSave.Location = new System.Drawing.Point(160, 22);
            this.rdoServerSave.Name = "rdoServerSave";
            this.rdoServerSave.Size = new System.Drawing.Size(74, 21);
            this.rdoServerSave.TabIndex = 2;
            this.rdoServerSave.TabStop = true;
            this.rdoServerSave.Text = "服务器上";
            this.rdoServerSave.UseVisualStyleBackColor = true;
            // 
            // rdoLocalSave
            // 
            this.rdoLocalSave.AutoSize = true;
            this.rdoLocalSave.Location = new System.Drawing.Point(6, 22);
            this.rdoLocalSave.Name = "rdoLocalSave";
            this.rdoLocalSave.Size = new System.Drawing.Size(50, 21);
            this.rdoLocalSave.TabIndex = 1;
            this.rdoLocalSave.TabStop = true;
            this.rdoLocalSave.Text = "本地";
            this.rdoLocalSave.UseVisualStyleBackColor = true;
            // 
            // label16
            // 
            this.label16.AutoSize = true;
            this.label16.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label16.Location = new System.Drawing.Point(5, 10);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(490, 21);
            this.label16.TabIndex = 0;
            this.label16.Text = "若不想把文件保存在本地，想保存到服务器上，请配置服务器信息。";
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.linkLabel2);
            this.tabPage2.Controls.Add(this.linkLabel1);
            this.tabPage2.Controls.Add(this.label11);
            this.tabPage2.Controls.Add(this.label10);
            this.tabPage2.Controls.Add(this.label7);
            this.tabPage2.Location = new System.Drawing.Point(4, 26);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(767, 521);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "关于";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // linkLabel2
            // 
            this.linkLabel2.AutoSize = true;
            this.linkLabel2.Location = new System.Drawing.Point(133, 78);
            this.linkLabel2.Name = "linkLabel2";
            this.linkLabel2.Size = new System.Drawing.Size(100, 17);
            this.linkLabel2.TabIndex = 4;
            this.linkLabel2.TabStop = true;
            this.linkLabel2.Text = "访问作者B站主页";
            this.linkLabel2.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel2_LinkClicked);
            // 
            // linkLabel1
            // 
            this.linkLabel1.AutoSize = true;
            this.linkLabel1.Location = new System.Drawing.Point(7, 78);
            this.linkLabel1.Name = "linkLabel1";
            this.linkLabel1.Size = new System.Drawing.Size(120, 17);
            this.linkLabel1.TabIndex = 3;
            this.linkLabel1.TabStop = true;
            this.linkLabel1.Text = "访问GitHub项目主页";
            this.linkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(7, 41);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(264, 17);
            this.label11.TabIndex = 2;
            this.label11.Text = "软件可能有未知BUG，请在GitHub上提出issure";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Font = new System.Drawing.Font("微软雅黑", 21.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label10.Location = new System.Drawing.Point(3, 3);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(307, 38);
            this.label10.TabIndex = 1;
            this.label10.Text = "U盘文件复制器 V1.5.0";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Font = new System.Drawing.Font("微软雅黑", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label7.Location = new System.Drawing.Point(6, 58);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(554, 20);
            this.label7.TabIndex = 0;
            this.label7.Text = "公告：此软件仅供学习，不可用于非法用途，欢迎支持B站原作者XEKernel，感谢使用！\r\n";
            // 
            // label20
            // 
            this.label20.AutoSize = true;
            this.label20.Location = new System.Drawing.Point(24, 117);
            this.label20.Name = "label20";
            this.label20.Size = new System.Drawing.Size(32, 17);
            this.label20.TabIndex = 6;
            this.label20.Text = "端口";
            // 
            // txtServerPort
            // 
            this.txtServerPort.Location = new System.Drawing.Point(67, 114);
            this.txtServerPort.Name = "txtServerPort";
            this.txtServerPort.Size = new System.Drawing.Size(167, 23);
            this.txtServerPort.TabIndex = 7;
            // 
            // btnTestConn
            // 
            this.btnTestConn.Location = new System.Drawing.Point(6, 143);
            this.btnTestConn.Name = "btnTestConn";
            this.btnTestConn.Size = new System.Drawing.Size(75, 23);
            this.btnTestConn.TabIndex = 8;
            this.btnTestConn.Text = "测试连接";
            this.btnTestConn.UseVisualStyleBackColor = true;
            // 
            // lblConnStatus
            // 
            this.lblConnStatus.AutoSize = true;
            this.lblConnStatus.Location = new System.Drawing.Point(6, 170);
            this.lblConnStatus.Name = "lblConnStatus";
            this.lblConnStatus.Size = new System.Drawing.Size(56, 17);
            this.lblConnStatus.TabIndex = 9;
            this.lblConnStatus.Text = "连接状态";
            // 
            // chkUseHttps
            // 
            this.chkUseHttps.AutoSize = true;
            this.chkUseHttps.Location = new System.Drawing.Point(9, 192);
            this.chkUseHttps.Name = "chkUseHttps";
            this.chkUseHttps.Size = new System.Drawing.Size(112, 21);
            this.chkUseHttps.TabIndex = 10;
            this.chkUseHttps.Text = "是否启用HTTPS";
            this.chkUseHttps.UseVisualStyleBackColor = true;
            // 
            // chkChunkedUpload
            // 
            this.chkChunkedUpload.AutoSize = true;
            this.chkChunkedUpload.Location = new System.Drawing.Point(127, 192);
            this.chkChunkedUpload.Name = "chkChunkedUpload";
            this.chkChunkedUpload.Size = new System.Drawing.Size(99, 21);
            this.chkChunkedUpload.TabIndex = 11;
            this.chkChunkedUpload.Text = "是否压缩上传";
            this.chkChunkedUpload.UseVisualStyleBackColor = true;
            // 
            // label22
            // 
            this.label22.AutoSize = true;
            this.label22.Location = new System.Drawing.Point(6, 216);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(56, 17);
            this.label22.TabIndex = 12;
            this.label22.Text = "分块大小";
            // 
            // txtChunkSize
            // 
            this.txtChunkSize.Location = new System.Drawing.Point(67, 213);
            this.txtChunkSize.Name = "txtChunkSize";
            this.txtChunkSize.Size = new System.Drawing.Size(87, 23);
            this.txtChunkSize.TabIndex = 13;
            // 
            // cmbChunkUnit
            // 
            this.cmbChunkUnit.FormattingEnabled = true;
            this.cmbChunkUnit.Items.AddRange(new object[] {
            "KB",
            "MB"});
            this.cmbChunkUnit.Location = new System.Drawing.Point(160, 213);
            this.cmbChunkUnit.Name = "cmbChunkUnit";
            this.cmbChunkUnit.Size = new System.Drawing.Size(66, 25);
            this.cmbChunkUnit.TabIndex = 14;
            this.cmbChunkUnit.Text = "MB";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(785, 558);
            this.Controls.Add(this.tabControl1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.Text = "U盘文件复制器  V1.5.0";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.groupBox6.ResumeLayout(false);
            this.groupBox6.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numSpeedMinutes)).EndInit();
            this.groupBox4.ResumeLayout(false);
            this.groupBox4.PerformLayout();
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.groupBox9.ResumeLayout(false);
            this.groupBox9.PerformLayout();
            this.groupBox8.ResumeLayout(false);
            this.groupBox8.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxDepth)).EndInit();
            this.groupBox7.ResumeLayout(false);
            this.groupBox7.PerformLayout();
            this.groupBox5.ResumeLayout(false);
            this.groupBox5.PerformLayout();
            this.tabPage3.ResumeLayout(false);
            this.tabPage3.PerformLayout();
            this.grpServerConfig.ResumeLayout(false);
            this.grpServerConfig.PerformLayout();
            this.groupBox10.ResumeLayout(false);
            this.groupBox10.PerformLayout();
            this.tabPage2.ResumeLayout(false);
            this.tabPage2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.TextBox txtTargetDir;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.CheckBox chkCompressed;
        private System.Windows.Forms.CheckBox chkAllFiles;
        private System.Windows.Forms.TextBox txtCustomExtensions;
        private System.Windows.Forms.CheckBox chkCustomExt;
        private System.Windows.Forms.CheckBox chkVideo;
        private System.Windows.Forms.CheckBox chkImage;
        private System.Windows.Forms.CheckBox chkPdf;
        private System.Windows.Forms.CheckBox chkExcel;
        private System.Windows.Forms.CheckBox chkWord;
        private System.Windows.Forms.CheckBox chkPpt;
        private System.Windows.Forms.Label lblCount;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.RichTextBox txtLogView;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button btnBrowseDir;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button btnHide;
        private System.Windows.Forms.CheckBox chkAudio;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.RadioButton rdoReplaceNewer;
        private System.Windows.Forms.RadioButton rdoKeepBoth;
        private System.Windows.Forms.RadioButton rdoOverwrite;
        private System.Windows.Forms.RadioButton rdoSkip;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox txtMaxSizeMB;
        private System.Windows.Forms.CheckBox chkSizeLimit;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.TextBox txtFileNameKeywords;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.CheckBox chkFileNameFilter;
        private System.Windows.Forms.GroupBox groupBox6;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.NumericUpDown numSpeedMinutes;
        private System.Windows.Forms.CheckBox chkSpeedLimit;
        private System.Windows.Forms.ComboBox cmbSpeedLimit;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.LinkLabel linkLabel1;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.LinkLabel linkLabel2;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.TextBox txtReverseCopyFile;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.CheckBox chkReverseCopy;
        private System.Windows.Forms.TextBox txtStopCopyFile;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.CheckBox chkStopCopy;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.GroupBox groupBox7;
        private System.Windows.Forms.CheckBox chkLogNeutral;
        private System.Windows.Forms.CheckBox chkLogErrors;
        private System.Windows.Forms.CheckBox chkLogSuccess;
        private System.Windows.Forms.CheckBox chkLogWindow;
        private System.Windows.Forms.CheckBox chkLogToFile;
        private System.Windows.Forms.GroupBox grpNotify;
        private System.Windows.Forms.CheckBox chkTrayIcon;
        private System.Windows.Forms.CheckBox chkNotify;
        private System.Windows.Forms.GroupBox grpWhitelist;
        private System.Windows.Forms.Label label24;
        private System.Windows.Forms.TextBox txtWhitelist;
        private System.Windows.Forms.CheckBox chkWhitelist;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.CheckBox chkFolderFilter;
        private System.Windows.Forms.TextBox txtFolderKeywords;
        private System.Windows.Forms.GroupBox groupBox8;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.CheckBox chkDirectoryTree;
        private System.Windows.Forms.NumericUpDown numMaxDepth;
        private System.Windows.Forms.CheckBox chkDepthLimit;
        private System.Windows.Forms.GroupBox groupBox9;
        private System.Windows.Forms.CheckBox chkAutoStart;
        private System.Windows.Forms.CheckBox chkAutoStartHidden;
        private System.Windows.Forms.TabPage tabPage3;
        private System.Windows.Forms.GroupBox groupBox10;
        private System.Windows.Forms.RadioButton rdoServerSave;
        private System.Windows.Forms.RadioButton rdoLocalSave;
        private System.Windows.Forms.Label label16;
        private System.Windows.Forms.GroupBox grpServerConfig;
        private System.Windows.Forms.TextBox txtServerPassword;
        private System.Windows.Forms.Label label18;
        private System.Windows.Forms.TextBox txtServerAddress;
        private System.Windows.Forms.Label label17;
        private System.Windows.Forms.TextBox txtServerToken;
        private System.Windows.Forms.Label label19;
        private System.Windows.Forms.TextBox txtServerPort;
        private System.Windows.Forms.Label label20;
        private System.Windows.Forms.ComboBox cmbChunkUnit;
        private System.Windows.Forms.TextBox txtChunkSize;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.CheckBox chkChunkedUpload;
        private System.Windows.Forms.CheckBox chkUseHttps;
        private System.Windows.Forms.Label lblConnStatus;
        private System.Windows.Forms.Button btnTestConn;
        private System.Windows.Forms.Button _btnBrowseRemote;
    }
}

