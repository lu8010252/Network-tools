using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
#nullable disable

namespace NeworkTool
{
    /// <summary>
    /// 摄像头登录窗口：登录后可以预览画面（快照刷新）、查看设备信息、修改 IP / 掩码 / 网关 / DNS、重启设备。
    /// 协议：ONVIF（通用，WS-Security 摘要认证）、海康 ISAPI、大华 CGI；按品牌决定先试哪个，失败再试其它。
    /// 账号密码只保存在本窗口内存里，窗口关闭即丢弃，不写入任何文件。
    /// </summary>
    public class CameraLoginForm : Form
    {
        private readonly string ip;
        private readonly string brand;

        private TextBox txtUser, txtPass, txtNewIp, txtMask, txtGw, txtDns, txtInfo, txtChannel;
        private Button btnRename;
        private Panel previewHost;
        private ComboBox cmbPort;
        private Button btnLogin, btnApply, btnReboot, btnCopyRtsp, btnRefresh;
        private CheckBox chkLive;
        private PictureBox pic;
        private Label lblStatus;
        private System.Windows.Forms.Timer timer;

        private HttpClient http;
        private string mode;            // "ONVIF" / "HIK" / "DAHUA"
        private string baseUrl;         // http(s)://ip:port
        private string mediaUrl, profileToken, ifaceToken, snapshotUrl, rtspUrl;
        private TimeSpan timeOffset = TimeSpan.Zero;
        private string user, pass;
        private bool busyFrame;

        private static readonly Font fNormal = new Font("Microsoft YaHei UI", 9F);
        private static readonly Font fBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        // 视频播放(LibVLC)。没有 libvlc 原生库时自动退回快照预览。
        private static bool vlcCoreReady, vlcCoreFailed;
        private LibVLC libVlc;
        private MediaPlayer vlcPlayer;
        private VideoView videoView;
        private string autoUser, autoPass;
        /// <summary>批量登录用：不预览、不启动定时器，只验证账号并读取设备信息。</summary>
        public bool Headless;
        public string Protocol { get { return mode; } }
        public string InfoText { get { return txtInfo.Text; } }

        public CameraLoginForm(string ip, string brand, int webPort, string openPorts) : this(ip, brand, webPort, openPorts, null, null) { }

        /// <summary>带账号密码构造：窗口显示后自动登录(批量登录后双击进入用)。</summary>
        public CameraLoginForm(string ip, string brand, int webPort, string openPorts, string user, string pass)
        {
            this.ip = ip; this.brand = brand ?? "";
            BuildUi(webPort, openPorts);
            if (pass != null)
            {
                autoUser = user; autoPass = pass;
                txtUser.Text = user; txtPass.Text = pass;
                Shown += async (s, e) => await LoginAsync();
            }
        }

        public async Task<bool> HeadlessLoginAsync(string u, string p)
        {
            Headless = true;
            txtUser.Text = u; txtPass.Text = p;
            await LoginAsync();
            return mode != null;
        }

        #region ---- UI ----
        private void BuildUi(int webPort, string openPorts)
        {
            // 用 DPI 缩放 + 布局面板(自动排版)，高分屏/125%/150% 缩放下文字不再重叠
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = $"设备登录 - {ip}  {brand}";
            Size = new Size(1060, 720);
            MinimumSize = new Size(960, 660);
            StartPosition = FormStartPosition.CenterParent;
            Font = fNormal;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            // ---- 登录行 ----
            var top = new GroupBox { Text = " 登录 ", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = fBold, Padding = new Padding(8, 4, 8, 8) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Font = fNormal };
            Func<string, Label> lbl = t => new Label { Text = t, AutoSize = true, Margin = new Padding(0, 8, 4, 0), Font = fNormal };
            flow.Controls.Add(lbl("IP:"));
            flow.Controls.Add(new Label { Text = ip, AutoSize = true, Margin = new Padding(0, 8, 18, 0), Font = fBold });
            flow.Controls.Add(lbl("端口:"));
            cmbPort = new ComboBox { Width = 80, Margin = new Padding(0, 4, 18, 0), Font = fNormal };
            var ports = new List<string>();
            if (webPort > 0) ports.Add(webPort.ToString());
            foreach (var p in (openPorts ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n; if (int.TryParse(p.Trim(), out n) && new[] { 80, 81, 8080, 8081, 8088, 8888, 8899, 2020, 5000, 443, 8443 }.Contains(n) && !ports.Contains(n.ToString())) ports.Add(n.ToString());
            }
            if (ports.Count == 0) ports.Add("80");
            cmbPort.Items.AddRange(ports.ToArray());
            cmbPort.Text = ports[0];
            flow.Controls.Add(cmbPort);
            flow.Controls.Add(lbl("用户名:"));
            txtUser = new TextBox { Width = 120, Text = "admin", Margin = new Padding(0, 4, 18, 0), Font = fNormal };
            flow.Controls.Add(txtUser);
            flow.Controls.Add(lbl("密码:"));
            txtPass = new TextBox { Width = 140, UseSystemPasswordChar = true, Margin = new Padding(0, 4, 18, 0), Font = fNormal };
            flow.Controls.Add(txtPass);
            btnLogin = new Button { Text = "登录", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 30), Margin = new Padding(0, 2, 18, 0), Font = fBold };
            flow.Controls.Add(btnLogin);
            flow.SetFlowBreak(btnLogin, true);
            lblStatus = new Label { Text = "请输入账号密码后登录", AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 8, 0, 0), ForeColor = Color.DimGray, Font = fNormal };
            flow.Controls.Add(lblStatus);
            top.Controls.Add(flow);
            root.Controls.Add(top, 0, 0);

            // ---- 主区域：左 预览+信息，右 设置 ----
            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410F));
            root.Controls.Add(main, 0, 1);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 10, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            main.Controls.Add(left, 0, 0);

            previewHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Margin = new Padding(0) };
            pic = new PictureBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            previewHost.Controls.Add(pic);
            left.Controls.Add(previewHost, 0, 0);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };
            chkLive = new CheckBox { Text = "实时刷新预览(约每秒1张快照)", AutoSize = true, Checked = true, Enabled = false, Margin = new Padding(0, 6, 14, 0), Font = fNormal };
            btnRefresh = new Button { Text = "刷新一张", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(84, 30), Enabled = false, Margin = new Padding(0, 0, 8, 0), Font = fNormal };
            btnCopyRtsp = new Button { Text = "复制 RTSP 地址", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(110, 30), Enabled = false, Margin = new Padding(0), Font = fNormal };
            bar.Controls.Add(chkLive); bar.Controls.Add(btnRefresh); bar.Controls.Add(btnCopyRtsp);
            left.Controls.Add(bar, 0, 1);

            txtInfo = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = fNormal, Margin = new Padding(0) };
            left.Controls.Add(txtInfo, 0, 2);

            // 右侧：分页(网络 / 画面 / 维护)，功能多了用分页放，窗口不会被撑坏
            var tabs = new TabControl { Dock = DockStyle.Fill, Font = fNormal, Margin = new Padding(0) };
            Func<string, FlowLayoutPanel> addPage = title =>
            {
                var pg = new TabPage(title) { Padding = new Padding(6), UseVisualStyleBackColor = true };
                var fl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
                pg.Controls.Add(fl); tabs.TabPages.Add(pg); return fl;
            };
            var pageNet = addPage("网络");
            var pageVideo = addPage("画面");
            var pageMaint = addPage("维护");
            main.Controls.Add(tabs, 1, 0);

            Func<string, Control, GroupBox> mkGroup = (title, content) =>
            {
                var g = new GroupBox { Text = " " + title + " ", Width = 372, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = fBold, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(0, 0, 0, 10) };
                content.Dock = DockStyle.Fill; g.Controls.Add(content); return g;
            };
            Func<FlowLayoutPanel> mkCol = () => new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            Func<FlowLayoutPanel> mkRow = () => new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
            Func<string, int, bool, Button> mkBtn = (text, w, bold) =>
            {
                var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(w, 32), Enabled = false, Font = bold ? fBold : fNormal, Margin = new Padding(0, 0, 8, 6) };
                loginControls.Add(b); return b;
            };
            Func<string, Label> mkTip = t => new Label { Text = t, AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = Color.DimGray, Font = fNormal, Margin = new Padding(0, 4, 0, 4) };
            Func<string, CheckBox> mkChk = t => { var c = new CheckBox { Text = t, AutoSize = true, Enabled = false, Font = fNormal, Margin = new Padding(0, 4, 0, 4) }; loginControls.Add(c); return c; };
            Func<TableLayoutPanel> mkTable = () => { var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink }; t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); return t; };
            Action<TableLayoutPanel, string, Control> addTblRow = (t, label, ctl) =>
            {
                int row = t.RowCount; t.RowCount = row + 1; t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6), Font = fNormal }, 0, row);
                ctl.Anchor = AnchorStyles.Left | AnchorStyles.Right; ctl.Margin = new Padding(0, 4, 0, 4);
                t.Controls.Add(ctl, 1, row);
            };

            // ===== 网络页 =====
            var nt = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            nt.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            nt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            int r = 0;
            Func<string, TextBox> addRow = (label) =>
            {
                nt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                nt.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6), Font = fNormal }, 0, r);
                var tb = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 230, Margin = new Padding(0, 4, 0, 4), Font = fNormal, Enabled = false };
                nt.Controls.Add(tb, 1, r); r++; return tb;
            };
            nt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            chkDhcp = mkChk("自动获取 IP (DHCP)");
            nt.Controls.Add(chkDhcp, 0, r); nt.SetColumnSpan(chkDhcp, 2); r++;
            txtNewIp = addRow("IP 地址:");
            txtMask = addRow("子网掩码:");
            txtGw = addRow("默认网关:");
            txtDns = addRow("DNS:");
            nt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var tip = mkTip("改 IP 会改为静态地址(勾选 DHCP 则改回自动获取)；应用后设备会断开，需要用新 IP 重新扫描/登录。请确认新 IP 不冲突。");
            nt.Controls.Add(tip, 0, r); nt.SetColumnSpan(tip, 2); r++;
            nt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            btnApply = mkBtn("应用修改", 100, true);
            nt.Controls.Add(btnApply, 0, r); nt.SetColumnSpan(btnApply, 2);
            pageNet.Controls.Add(mkGroup("网络设置(登录后可用)", nt));

            var ct = mkCol();
            txtChannel = new TextBox { Width = 340, Enabled = false, Font = fNormal, Margin = new Padding(0, 4, 0, 6) };
            btnRename = mkBtn("修改通道名称", 120, true);
            ct.Controls.Add(txtChannel); ct.Controls.Add(btnRename);
            ct.Controls.Add(mkTip("海康/大华改通道名；ONVIF 设备改画面叠加文字，设备不支持时改设备名称。修改后画面标题会更新。"));
            pageNet.Controls.Add(mkGroup("通道名称(画面上显示的名字)", ct));

            // ===== 画面页 =====
            var sc = mkCol();
            btnSnap = mkBtn("📷 抓拍保存...", 120, true);
            sc.Controls.Add(btnSnap);
            sc.Controls.Add(mkTip("把当前画面存成 JPG 图片(优先取设备快照，没有快照时抓当前视频帧)。"));
            pageVideo.Controls.Add(mkGroup("抓拍", sc));

            var oc = mkCol();
            chkOsdName = mkChk("画面上显示通道名称");
            chkOsdTime = mkChk("画面上显示日期时间");
            btnOsdApply = mkBtn("应用 OSD 设置", 120, true);
            oc.Controls.Add(chkOsdName); oc.Controls.Add(chkOsdTime); oc.Controls.Add(btnOsdApply);
            oc.Controls.Add(mkTip("仅海康 / 大华可用；ONVIF 设备暂不支持。"));
            pageVideo.Controls.Add(mkGroup("画面叠加(OSD)", oc));

            var st = mkTable();
            txtEnc = new TextBox { ReadOnly = true, Font = fNormal };
            txtRes = new TextBox { ReadOnly = true, Font = fNormal };
            numFps = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 25, Width = 110, Enabled = false, Font = fNormal };
            numBr = new NumericUpDown { Minimum = 32, Maximum = 32768, Increment = 128, Value = 4096, Width = 110, Enabled = false, Font = fNormal };
            loginControls.Add(numFps); loginControls.Add(numBr);
            addTblRow(st, "编码:", txtEnc);
            addTblRow(st, "分辨率:", txtRes);
            addTblRow(st, "帧率(fps):", numFps);
            addTblRow(st, "码率(kbps):", numBr);
            btnStreamApply = mkBtn("应用码流设置", 120, true);
            int sr = st.RowCount; st.RowCount = sr + 1; st.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            st.Controls.Add(btnStreamApply, 0, sr); st.SetColumnSpan(btnStreamApply, 2);
            int sr2 = st.RowCount; st.RowCount = sr2 + 1; st.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var stTip = mkTip("显示主码流参数；海康 / 大华可改帧率和码率，ONVIF 仅显示。修改后编码器会重新初始化，画面可能中断几秒。");
            st.Controls.Add(stTip, 0, sr2); st.SetColumnSpan(stTip, 2);
            pageVideo.Controls.Add(mkGroup("码流(主码流)", st));

            var pad = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            Func<string, string, Button> mkPtz = (text, dir) =>
            {
                var b = new Button { Text = text, Size = new Size(86, 34), Enabled = false, Font = fNormal, Margin = new Padding(2) };
                loginControls.Add(b);
                b.MouseDown += async (s, e) => await PtzSafeAsync(dir, true);
                b.MouseUp += async (s, e) => await PtzSafeAsync(dir, false);
                return b;
            };
            var ptzStop = new Button { Text = "■", Size = new Size(86, 34), Enabled = false, Font = fNormal, Margin = new Padding(2) };
            loginControls.Add(ptzStop);
            ptzStop.Click += async (s, e) => await PtzSafeAsync("S", false);
            pad.Controls.Add(mkPtz("↑", "U"), 1, 0); pad.Controls.Add(mkPtz("变倍＋", "I"), 2, 0);
            pad.Controls.Add(mkPtz("←", "L"), 0, 1); pad.Controls.Add(ptzStop, 1, 1); pad.Controls.Add(mkPtz("→", "R"), 2, 1);
            pad.Controls.Add(mkPtz("↓", "D"), 1, 2); pad.Controls.Add(mkPtz("变倍－", "O"), 2, 2);
            var pc = mkCol();
            pc.Controls.Add(pad);
            pc.Controls.Add(mkTip("按住方向键转动，松开即停；只有带云台/变焦的摄像机才有效。"));
            pageVideo.Controls.Add(mkGroup("云台 / 变焦", pc));

            // ===== 维护页 =====
            var tc = mkCol();
            lblDevTime = new Label { Text = "设备时间: (登录后显示)", AutoSize = true, MaximumSize = new Size(340, 0), Font = fNormal, Margin = new Padding(0, 4, 0, 6) };
            var tRow = mkRow();
            btnSyncTime = mkBtn("同步为本机时间", 120, true);
            btnReadTime = mkBtn("刷新", 70, false);
            tRow.Controls.Add(btnSyncTime); tRow.Controls.Add(btnReadTime);
            tc.Controls.Add(lblDevTime); tc.Controls.Add(tRow);
            tc.Controls.Add(mkTip("录像时间不对时一键校准；同步会把设备改为手动对时。"));
            pageMaint.Controls.Add(mkGroup("设备时间", tc));

            var pw = mkTable();
            txtNewPwd = new TextBox { UseSystemPasswordChar = true, Font = fNormal };
            txtNewPwd2 = new TextBox { UseSystemPasswordChar = true, Font = fNormal };
            loginControls.Add(txtNewPwd); loginControls.Add(txtNewPwd2);
            addTblRow(pw, "新密码:", txtNewPwd);
            addTblRow(pw, "再输一次:", txtNewPwd2);
            btnPwd = mkBtn("修改密码", 100, true);
            int pr = pw.RowCount; pw.RowCount = pr + 1; pw.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pw.Controls.Add(btnPwd, 0, pr); pw.SetColumnSpan(btnPwd, 2);
            int pr2 = pw.RowCount; pw.RowCount = pr2 + 1; pw.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var pwTip = mkTip("修改当前登录账号的密码，成功后自动用新密码重新登录。海康新固件对密码强度有要求(8 位以上、含多种字符)。");
            pw.Controls.Add(pwTip, 0, pr2); pw.SetColumnSpan(pwTip, 2);
            pageMaint.Controls.Add(mkGroup("修改登录密码", pw));

            var oc2 = mkRow();
            btnReboot = mkBtn("重启设备", 100, false);
            btnCopyInfo = mkBtn("复制设备信息", 110, false);
            btnOpenWeb = mkBtn("打开 Web 页面", 110, false);
            btnCopyRtspAuth = mkBtn("复制 RTSP(含账号密码)", 160, false);
            oc2.Controls.Add(btnReboot); oc2.Controls.Add(btnCopyInfo); oc2.Controls.Add(btnOpenWeb); oc2.Controls.Add(btnCopyRtspAuth);
            pageMaint.Controls.Add(mkGroup("设备操作", oc2));

            chkDhcp.CheckedChanged += (s, e) => UpdateNetFields();
            btnSnap.Click += async (s, e) => await SnapshotAsync();
            btnOsdApply.Click += async (s, e) => await ApplyOsdAsync();
            btnStreamApply.Click += async (s, e) => await ApplyStreamAsync();
            btnSyncTime.Click += async (s, e) => await SyncTimeAsync();
            btnReadTime.Click += async (s, e) => { try { await ReadTimeAsync(); } catch (Exception ex) { SetStatus("读取时间失败: " + Shorten(ex.Message), Color.Firebrick); } };
            btnPwd.Click += async (s, e) => await ChangePasswordAsync();
            btnCopyInfo.Click += (s, e) => { try { Clipboard.SetText(txtInfo.Text); SetStatus("设备信息已复制", Color.DarkGreen); } catch { } };
            btnOpenWeb.Click += (s, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(baseUrl) { UseShellExecute = true }); } catch (Exception ex) { SetStatus("打开失败: " + Shorten(ex.Message), Color.Firebrick); } };
            btnCopyRtspAuth.Click += (s, e) => { try { Clipboard.SetText(WithCreds(rtspUrl ?? "", user, pass)); SetStatus("已复制 RTSP 地址(含账号密码，请勿外传)", Color.DarkGreen); } catch { } };

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += async (s, e) => { if (chkLive.Checked) await RefreshFrameAsync(); };

            AcceptButton = btnLogin;
            btnLogin.Click += async (s, e) => await LoginAsync();
            btnRefresh.Click += async (s, e) => await RefreshFrameAsync();
            btnCopyRtsp.Click += (s, e) => { try { Clipboard.SetText(rtspUrl ?? ""); SetStatus("RTSP 地址已复制(不含密码，播放器里需填账号密码)", Color.DarkGreen); } catch { } };
            btnApply.Click += async (s, e) => await ApplyAsync();
            btnReboot.Click += async (s, e) => await RebootAsync();
            btnRename.Click += async (s, e) => await RenameChannelAsync();
            FormClosed += (s, e) => { StopVideo(); timer.Stop(); timer.Dispose(); http?.Dispose(); if (pic.Image != null) pic.Image.Dispose(); pass = null; };
        }

        private void SetStatus(string text, Color color) { lblStatus.Text = text; lblStatus.ForeColor = color; }
        private void SetLoggedIn(bool on)
        {
            foreach (var c in loginControls) c.Enabled = on;
            txtChannel.Enabled = btnRename.Enabled = on;
            chkLive.Enabled = btnRefresh.Enabled = on;
            btnCopyRtsp.Enabled = on && !string.IsNullOrEmpty(rtspUrl);
            btnCopyRtspAuth.Enabled = on && !string.IsNullOrEmpty(rtspUrl);
            btnSnap.Enabled = on;
            if (on) UpdateNetFields(); else txtNewIp.Enabled = txtMask.Enabled = txtGw.Enabled = txtDns.Enabled = false;
        }
        #endregion

        #region ---- 登录 ----
        private async Task LoginAsync()
        {
            user = txtUser.Text.Trim(); pass = txtPass.Text;
            if (user.Length == 0) { SetStatus("请输入用户名", Color.Firebrick); return; }

            btnLogin.Enabled = false; timer.Stop(); StopVideo(); SetLoggedIn(false);
            SetStatus("正在登录...", Color.DimGray);
            http?.Dispose();
            http = new HttpClient(new HttpClientHandler
            {
                Credentials = new NetworkCredential(user, pass),   // 海康/大华 HTTP 摘要认证、部分设备的 Basic 认证
                ServerCertificateCustomValidationCallback = (m, c, ch, er) => true,
                AllowAutoRedirect = false
            })
            { Timeout = TimeSpan.FromSeconds(8) };

            var portList = new List<string> { cmbPort.Text.Trim() };
            foreach (var o in cmbPort.Items) if (!portList.Contains(o.ToString())) portList.Add(o.ToString());

            // 品牌决定尝试顺序
            var order = brand.Contains("海康") ? new[] { "HIK", "ONVIF", "DAHUA" }
                      : brand.Contains("大华") ? new[] { "DAHUA", "ONVIF", "HIK" }
                      : new[] { "ONVIF", "HIK", "DAHUA" };

            var errors = new List<string>();
            bool ok = false;
            foreach (var p in portList)
            {
                int port; if (!int.TryParse(p, out port)) continue;
                baseUrl = ((port == 443 || port == 8443) ? "https://" : "http://") + ip + (port == 80 || port == 443 ? "" : ":" + port);
                foreach (var m in order)
                {
                    try
                    {
                        mode = m;
                        if (m == "ONVIF") await LoginOnvifAsync();
                        else if (m == "HIK") await LoginHikAsync();
                        else await LoginDahuaAsync();
                        ok = true; break;
                    }
                    catch (UnauthorizedAccessException) { errors.Add($"{p}/{m}: 账号或密码错误"); }
                    catch (Exception ex) { errors.Add($"{p}/{m}: {Shorten(ex.Message)}"); }
                }
                if (ok) break;
            }

            btnLogin.Enabled = true;
            if (!ok)
            {
                mode = null;
                SetStatus("登录失败", Color.Firebrick);
                txtInfo.Text = "登录失败，各通道结果：\r\n" + string.Join("\r\n", errors) +
                    "\r\n\r\n提示：海康/大华默认要先在 Web 页面激活设置密码；杂牌设备需开启 ONVIF；ONVIF 通道对时间偏差敏感(已自动校正)。";
                return;
            }

            SetStatus($"已登录 ({mode} @ {baseUrl})", Color.DarkGreen);
            SetLoggedIn(true);
            if (Headless) return;
            await LoadExtrasAsync();
            SetLoggedIn(true);
            if (!TryStartVideo())
            {
                await RefreshFrameAsync();
                timer.Start();
            }
        }

        private static string WithCreds(string url, string u, string p)
        {
            if (string.IsNullOrEmpty(url) || url.IndexOf('@') >= 0) return url;
            return Regex.Replace(url, "^rtsp://", "rtsp://" + Uri.EscapeDataString(u) + ":" + Uri.EscapeDataString(p ?? "") + "@", RegexOptions.IgnoreCase);
        }

        /// <summary>优先用 LibVLC 播放 RTSP 实时视频；失败(缺 libvlc 等)返回 false，由调用方退回快照预览。</summary>
        private bool TryStartVideo()
        {
            if (string.IsNullOrEmpty(rtspUrl) || vlcCoreFailed) return false;
            try
            {
                if (!vlcCoreReady) { Core.Initialize(); vlcCoreReady = true; }
                StopVideo();
                if (videoView == null)
                {
                    videoView = new VideoView { Dock = DockStyle.Fill, BackColor = Color.Black };
                    previewHost.Controls.Add(videoView);
                }
                libVlc = new LibVLC("--network-caching=300", "--rtsp-tcp");
                vlcPlayer = new MediaPlayer(libVlc);
                videoView.MediaPlayer = vlcPlayer;
                videoView.Visible = true; pic.Visible = false;
                chkLive.Visible = false; btnRefresh.Visible = false;
                vlcPlayer.Play(new Media(libVlc, WithCreds(rtspUrl, user, pass), FromType.FromLocation));
                SetStatus($"已登录 ({mode} @ {baseUrl}) · 实时视频", Color.DarkGreen);
                return true;
            }
            catch (Exception ex)
            {
                vlcCoreFailed = true;
                StopVideo();
                if (videoView != null) videoView.Visible = false;
                pic.Visible = true; chkLive.Visible = true; btnRefresh.Visible = true;
                txtInfo.AppendText("\r\n[视频播放不可用，已改用快照预览: " + Shorten(ex.Message) + "]");
                return false;
            }
        }

        private void StopVideo()
        {
            try { vlcPlayer?.Stop(); } catch { }
            try { if (videoView != null) videoView.MediaPlayer = null; } catch { }
            try { vlcPlayer?.Dispose(); } catch { }
            try { libVlc?.Dispose(); } catch { }
            vlcPlayer = null; libVlc = null;
        }

        private static string Shorten(string s) { s = Regex.Replace(s ?? "", @"\s+", " "); return s.Length > 90 ? s.Substring(0, 90) + "..." : s; }
        private static string RxCs(string text, string pattern) { var m = Regex.Match(text ?? "", pattern, RegexOptions.Singleline); return m.Success ? m.Groups[1].Value : ""; }
        private static string Rx(string text, string pattern) { var m = Regex.Match(text ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline); return m.Success ? m.Groups[1].Value.Trim() : ""; }

        private async Task<string> HttpGetAsync(string path)
        {
            using (var resp = await http.GetAsync(baseUrl + path))
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden) throw new UnauthorizedAccessException();
                if (!resp.IsSuccessStatusCode) throw new Exception("HTTP " + (int)resp.StatusCode);
                return await resp.Content.ReadAsStringAsync();
            }
        }

        private async Task<string> HttpPutXmlAsync(string path, string xml)
        {
            using (var content = new StringContent(xml, Encoding.UTF8, "application/xml"))
            using (var resp = await http.PutAsync(baseUrl + path, content))
            {
                string body = await resp.Content.ReadAsStringAsync();
                if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
                if (!resp.IsSuccessStatusCode) throw new Exception("HTTP " + (int)resp.StatusCode + " " + Shorten(body));
                return body;
            }
        }

        private async Task<byte[]> HttpGetBytesAsync(string url)
        {
            using (var resp = await http.GetAsync(url))
            {
                if (!resp.IsSuccessStatusCode) throw new Exception("HTTP " + (int)resp.StatusCode);
                return await resp.Content.ReadAsByteArrayAsync();
            }
        }
        #endregion

        #region ---- ONVIF ----
        private const string NsDev = "http://www.onvif.org/ver10/device/wsdl";
        private const string NsMedia = "http://www.onvif.org/ver10/media/wsdl";
        private const string NsSch = "http://www.onvif.org/ver10/schema";

        private string OnvifHeader()
        {
            var nonce = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
            string created = (DateTime.UtcNow + timeOffset).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            byte[] createdB = Encoding.UTF8.GetBytes(created), passB = Encoding.UTF8.GetBytes(pass ?? "");
            var all = new byte[nonce.Length + createdB.Length + passB.Length];
            Buffer.BlockCopy(nonce, 0, all, 0, nonce.Length);
            Buffer.BlockCopy(createdB, 0, all, nonce.Length, createdB.Length);
            Buffer.BlockCopy(passB, 0, all, nonce.Length + createdB.Length, passB.Length);
            string digest;
            using (var sha = SHA1.Create()) digest = Convert.ToBase64String(sha.ComputeHash(all));
            return "<s:Header><wsse:Security xmlns:wsse=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\" xmlns:wsu=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\"><wsse:UsernameToken>" +
                   "<wsse:Username>" + SecurityElement.Escape(user) + "</wsse:Username>" +
                   "<wsse:Password Type=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest\">" + digest + "</wsse:Password>" +
                   "<wsse:Nonce EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">" + Convert.ToBase64String(nonce) + "</wsse:Nonce>" +
                   "<wsu:Created>" + created + "</wsu:Created></wsse:UsernameToken></wsse:Security></s:Header>";
        }

        private async Task<string> SoapAsync(string url, string bodyXml, bool auth = true)
        {
            string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:tds=\"" + NsDev + "\" xmlns:trt=\"" + NsMedia + "\" xmlns:tt=\"" + NsSch + "\">" +
                         (auth ? OnvifHeader() : "") + "<s:Body>" + bodyXml + "</s:Body></s:Envelope>";
            using (var content = new StringContent(xml, Encoding.UTF8, "application/soap+xml"))
            using (var resp = await http.PostAsync(url, content))
            {
                string body = await resp.Content.ReadAsStringAsync();
                if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
                if (body.IndexOf("NotAuthorized", StringComparison.OrdinalIgnoreCase) >= 0 || body.IndexOf("Sender not Authorized", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new UnauthorizedAccessException();
                if (body.IndexOf("Envelope", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("不是 ONVIF 响应(HTTP " + (int)resp.StatusCode + ")");
                if (body.IndexOf(":Fault>", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("设备返回错误: " + Shorten(Rx(body, @"Text[^>]*>([^<]+)<")));
                return body;
            }
        }

        private string FixHost(string url)
        {
            // 设备返回的地址有时是内网别的地址/域名，统一换成我们实际连接的 IP
            try { var u = new Uri(url); return new UriBuilder(u) { Host = ip }.Uri.ToString(); } catch { return url; }
        }

        private async Task LoginOnvifAsync()
        {
            string dev = baseUrl + "/onvif/device_service";
            // 先取设备时间，纠正 WS-Security 时间戳偏差（很多设备要求时间差在几分钟内）
            try
            {
                string t = await SoapAsync(dev, "<tds:GetSystemDateAndTime/>", false);
                string utc = Rx(t, @"UTCDateTime>(.*?)</[^>]*UTCDateTime>");
                if (utc.Length == 0) utc = t;
                int Y = int.Parse(Rx(utc, @"Year>(\d+)<")), Mo = int.Parse(Rx(utc, @"Month>(\d+)<")), D = int.Parse(Rx(utc, @"Day>(\d+)<"));
                int H = int.Parse(Rx(utc, @"Hour>(\d+)<")), Mi = int.Parse(Rx(utc, @"Minute>(\d+)<")), S = int.Parse(Rx(utc, @"Second>(\d+)<"));
                timeOffset = new DateTime(Y, Mo, D, H, Mi, S, DateTimeKind.Utc) - DateTime.UtcNow;
            }
            catch (UnauthorizedAccessException) { throw; }
            catch { timeOffset = TimeSpan.Zero; }

            string info = await SoapAsync(dev, "<tds:GetDeviceInformation/>");
            var sb = new StringBuilder();
            sb.AppendLine("协议: ONVIF");
            sb.AppendLine("厂商: " + Rx(info, @"Manufacturer>([^<]*)<"));
            sb.AppendLine("型号: " + Rx(info, @"Model>([^<]*)<"));
            sb.AppendLine("固件: " + Rx(info, @"FirmwareVersion>([^<]*)<"));
            sb.AppendLine("序列号: " + Rx(info, @"SerialNumber>([^<]*)<"));

            // 媒体服务地址
            mediaUrl = baseUrl + "/onvif/media_service";
            try
            {
                string caps = await SoapAsync(dev, "<tds:GetCapabilities><tds:Category>All</tds:Category></tds:GetCapabilities>");
                string m = Rx(caps, @"<(?:\w+:)?Media>\s*<(?:\w+:)?XAddr>([^<]+)<");
                if (m.Length > 0) mediaUrl = FixHost(m);
            }
            catch (UnauthorizedAccessException) { throw; }
            catch { }

            snapshotUrl = null; rtspUrl = null; profileToken = null;
            try
            {
                string prof = await SoapAsync(mediaUrl, "<trt:GetProfiles/>");
                profileToken = Rx(prof, @"Profiles[^>]*\btoken=""([^""]+)""");
                if (profileToken.Length > 0)
                {
                    try
                    {
                        string s = await SoapAsync(mediaUrl, "<trt:GetSnapshotUri><trt:ProfileToken>" + profileToken + "</trt:ProfileToken></trt:GetSnapshotUri>");
                        string u = Rx(s, @"Uri>([^<]+)<").Replace("&amp;", "&");
                        if (u.Length > 0) snapshotUrl = FixHost(u);
                    }
                    catch (UnauthorizedAccessException) { throw; }
                    catch { }
                    try
                    {
                        string s = await SoapAsync(mediaUrl, "<trt:GetStreamUri><trt:StreamSetup><tt:Stream>RTP-Unicast</tt:Stream><tt:Transport><tt:Protocol>RTSP</tt:Protocol></tt:Transport></trt:StreamSetup><trt:ProfileToken>" + profileToken + "</trt:ProfileToken></trt:GetStreamUri>");
                        string u = Rx(s, @"Uri>([^<]+)<").Replace("&amp;", "&");
                        if (u.Length > 0) rtspUrl = FixHost(u);
                    }
                    catch (UnauthorizedAccessException) { throw; }
                    catch { }
                }
            }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex) { sb.AppendLine("媒体服务不可用: " + Shorten(ex.Message)); }
            if (snapshotUrl == null) sb.AppendLine("该设备未提供快照地址，无法预览(可复制 RTSP 地址用播放器看)");
            if (rtspUrl != null) sb.AppendLine("RTSP: " + rtspUrl);

            // 通道名称
            txtChannel.Text = "";
            try { txtChannel.Text = await ReadOnvifChannelAsync(dev); } catch (UnauthorizedAccessException) { throw; } catch { }

            // 网络参数
            ifaceToken = null;
            try
            {
                string ni = await SoapAsync(dev, "<tds:GetNetworkInterfaces/>");
                ifaceToken = Rx(ni, @"NetworkInterfaces[^>]*\btoken=""([^""]+)""");
                string ipv4 = Regex.Match(ni, @"IPv4.*", RegexOptions.Singleline | RegexOptions.IgnoreCase).Value;
                txtNewIp.Text = Rx(ipv4, @"Address>(\d+\.\d+\.\d+\.\d+)<");
                int prefix; txtMask.Text = int.TryParse(Rx(ipv4, @"PrefixLength>(\d+)<"), out prefix) ? PrefixToMask(prefix) : "255.255.255.0";
                sb.AppendLine("DHCP: " + Rx(ipv4, @"DHCP>(true|false)<"));
            }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex) { sb.AppendLine("读取网络参数失败: " + Shorten(ex.Message)); }
            try { txtGw.Text = Rx(await SoapAsync(dev, "<tds:GetNetworkDefaultGateway/>"), @"IPv4Address>(\d+\.\d+\.\d+\.\d+)<"); } catch (UnauthorizedAccessException) { throw; } catch { }
            try { txtDns.Text = Rx(await SoapAsync(dev, "<tds:GetDNS/>"), @"IPv4Address>(\d+\.\d+\.\d+\.\d+)<"); } catch (UnauthorizedAccessException) { throw; } catch { }
            txtInfo.Text = sb.ToString();
        }

        private async Task ApplyOnvifAsync(string nip, string mask, string gw, string dns)
        {
            string dev = baseUrl + "/onvif/device_service";
            if (gw.Length > 0) await SoapAsync(dev, "<tds:SetNetworkDefaultGateway><tds:IPv4Address>" + gw + "</tds:IPv4Address></tds:SetNetworkDefaultGateway>");
            if (dns.Length > 0) await SoapAsync(dev, "<tds:SetDNS><tds:FromDHCP>false</tds:FromDHCP><tds:DNSManual><tt:Type>IPv4</tt:Type><tt:IPv4Address>" + dns + "</tt:IPv4Address></tds:DNSManual></tds:SetDNS>");
            if (string.IsNullOrEmpty(ifaceToken)) throw new Exception("没有读到网卡标识，无法修改 IP");
            await SoapAsync(dev, "<tds:SetNetworkInterfaces><tds:InterfaceToken>" + ifaceToken + "</tds:InterfaceToken><tds:NetworkInterface><tt:Enabled>true</tt:Enabled><tt:IPv4><tt:Enabled>true</tt:Enabled><tt:DHCP>false</tt:DHCP><tt:Manual><tt:Address>" + nip + "</tt:Address><tt:PrefixLength>" + MaskToPrefix(mask) + "</tt:PrefixLength></tt:Manual></tt:IPv4></tds:NetworkInterface></tds:SetNetworkInterfaces>");
        }
        #endregion

        #region ---- 海康 ISAPI ----
        private async Task LoginHikAsync()
        {
            string di = await HttpGetAsync("/ISAPI/System/deviceInfo");
            if (di.IndexOf("DeviceInfo", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("不是海康 ISAPI 响应");
            var sb = new StringBuilder();
            sb.AppendLine("协议: 海康 ISAPI");
            sb.AppendLine("名称: " + Rx(di, @"deviceName>([^<]*)<"));
            sb.AppendLine("型号: " + Rx(di, @"model>([^<]*)<"));
            sb.AppendLine("固件: " + Rx(di, @"firmwareVersion>([^<]*)<"));
            sb.AppendLine("序列号: " + Rx(di, @"serialNumber>([^<]*)<"));
            snapshotUrl = baseUrl + "/ISAPI/Streaming/channels/101/picture";
            rtspUrl = "rtsp://" + ip + ":554/Streaming/Channels/101";
            sb.AppendLine("RTSP: " + rtspUrl);
            txtChannel.Text = "";
            try { txtChannel.Text = Rx(await HttpGetAsync("/ISAPI/System/Video/inputs/channels/1"), @"<name>([^<]*)<"); } catch (UnauthorizedAccessException) { throw; } catch { }
            string ni = await HttpGetAsync("/ISAPI/System/Network/interfaces/1");
            txtNewIp.Text = Rx(RxCs(ni, @"<IPAddress>(.*?)</IPAddress>"), @"ipAddress>([^<]*)<");
            txtMask.Text = Rx(RxCs(ni, @"<IPAddress>(.*?)</IPAddress>"), @"subnetMask>([^<]*)<");
            txtGw.Text = Rx(RxCs(ni, @"<DefaultGateway>(.*?)</DefaultGateway>"), @"ipAddress>([^<]*)<");
            txtDns.Text = Rx(RxCs(ni, @"<PrimaryDNS>(.*?)</PrimaryDNS>"), @"ipAddress>([^<]*)<");
            sb.AppendLine("取址方式: " + Rx(ni, @"addressingType>([^<]*)<"));
            txtInfo.Text = sb.ToString();
        }

        private static string ReplaceInBlock(string xml, string block, string tag, string value)
        {
            return Regex.Replace(xml, "(<" + block + ">.*?<" + tag + ">)[^<]*(</" + tag + ">)", "${1}" + value + "${2}", RegexOptions.Singleline);
        }

        private async Task ApplyHikAsync(string nip, string mask, string gw, string dns)
        {
            string ni = await HttpGetAsync("/ISAPI/System/Network/interfaces/1");
            string x = ni;
            x = Regex.Replace(x, @"(<addressingType>)[^<]*(</addressingType>)", "${1}static${2}", RegexOptions.IgnoreCase);
            x = ReplaceInBlock(x, "IPAddress", "ipAddress", nip);
            x = ReplaceInBlock(x, "IPAddress", "subnetMask", mask);
            if (gw.Length > 0) x = ReplaceInBlock(x, "DefaultGateway", "ipAddress", gw);
            if (dns.Length > 0) x = ReplaceInBlock(x, "PrimaryDNS", "ipAddress", dns);
            await HttpPutXmlAsync("/ISAPI/System/Network/interfaces/1", x);
        }
        #endregion

        #region ---- 大华 CGI ----
        private async Task LoginDahuaAsync()
        {
            string si = await HttpGetAsync("/cgi-bin/magicBox.cgi?action=getSystemInfo");
            if (si.IndexOf("=", StringComparison.Ordinal) < 0) throw new Exception("不是大华 CGI 响应");
            var sb = new StringBuilder();
            sb.AppendLine("协议: 大华 CGI");
            sb.AppendLine("型号: " + Rx(si, @"deviceType=([^\r\n]*)"));
            sb.AppendLine("序列号: " + Rx(si, @"serialNumber=([^\r\n]*)"));
            snapshotUrl = baseUrl + "/cgi-bin/snapshot.cgi?channel=1";
            rtspUrl = "rtsp://" + ip + ":554/cam/realmonitor?channel=1&subtype=0";
            sb.AppendLine("RTSP: " + rtspUrl);
            txtChannel.Text = "";
            try { txtChannel.Text = Rx(await HttpGetAsync("/cgi-bin/configManager.cgi?action=getConfig&name=ChannelTitle"), @"ChannelTitle\[0\]\.Name=([^\r\n]*)"); } catch (UnauthorizedAccessException) { throw; } catch { }
            string cfg = await HttpGetAsync("/cgi-bin/configManager.cgi?action=getConfig&name=Network");
            txtNewIp.Text = Rx(cfg, @"Network\.eth0\.IPAddress=([^\r\n]*)");
            txtMask.Text = Rx(cfg, @"Network\.eth0\.SubnetMask=([^\r\n]*)");
            txtGw.Text = Rx(cfg, @"Network\.eth0\.DefaultGateway=([^\r\n]*)");
            txtDns.Text = Rx(cfg, @"Network\.eth0\.DnsServers\[0\]=([^\r\n]*)");
            sb.AppendLine("DHCP: " + Rx(cfg, @"Network\.eth0\.DhcpEnable=([^\r\n]*)"));
            txtInfo.Text = sb.ToString();
        }

        private async Task ApplyDahuaAsync(string nip, string mask, string gw, string dns)
        {
            string q = "/cgi-bin/configManager.cgi?action=setConfig&Network.eth0.DhcpEnable=false&Network.eth0.IPAddress=" + nip + "&Network.eth0.SubnetMask=" + mask;
            if (gw.Length > 0) q += "&Network.eth0.DefaultGateway=" + gw;
            if (dns.Length > 0) q += "&Network.eth0.DnsServers[0]=" + dns;
            string r = await HttpGetAsync(q);
            if (r.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("设备返回: " + Shorten(r));
        }
        #endregion

        #region ---- 通道名称 ----
        /// <summary>在 GetOSDs 返回里找“普通文字”类型的 OSD(通道名叠加)，日期时间类 OSD 不动。</summary>
        private static string FindPlainOsd(string xml)
        {
            foreach (Match m in Regex.Matches(xml ?? "", @"<(?:\w+:)?OSDs\b[^>]*>.*?</(?:\w+:)?OSDs>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                if (Regex.IsMatch(m.Value, @"TextString.*?Type>\s*Plain\s*<", RegexOptions.Singleline | RegexOptions.IgnoreCase) && m.Value.IndexOf("PlainText", StringComparison.OrdinalIgnoreCase) >= 0)
                    return m.Value;
            return null;
        }

        private async Task<string> ReadOnvifChannelAsync(string dev)
        {
            try
            {
                string blk = FindPlainOsd(await SoapAsync(mediaUrl, "<trt:GetOSDs/>"));
                if (blk != null) return WebUtility.HtmlDecode(Rx(blk, @"PlainText>([^<]*)<"));
            }
            catch (UnauthorizedAccessException) { throw; }
            catch { }
            string sc = await SoapAsync(dev, "<tds:GetScopes/>");
            string n = Rx(sc, @"onvif://www\.onvif\.org/name/([^<\s]+)");
            return n.Length > 0 ? Uri.UnescapeDataString(n) : "";
        }

        /// <summary>返回提示文字(说明实际改了哪里)。</summary>
        private async Task<string> RenameOnvifAsync(string name)
        {
            try
            {
                string blk = FindPlainOsd(await SoapAsync(mediaUrl, "<trt:GetOSDs/>"));
                if (blk != null)
                {
                    string token = Rx(blk.Substring(0, blk.IndexOf('>')), @"token=""([^""]+)""");
                    string inner = blk.Substring(blk.IndexOf('>') + 1);
                    inner = inner.Substring(0, inner.LastIndexOf("</", StringComparison.Ordinal));
                    inner = Regex.Replace(inner, @"<(/?)\w+:", "<$1tt:");
                    inner = Regex.Replace(inner, @"(<tt:PlainText>)[^<]*(</tt:PlainText>)", m => m.Groups[1].Value + SecurityElement.Escape(name) + m.Groups[2].Value, RegexOptions.IgnoreCase);
                    await SoapAsync(mediaUrl, "<trt:SetOSD><trt:OSD token=\"" + token + "\">" + inner + "</trt:OSD></trt:SetOSD>");
                    return "画面叠加文字已修改";
                }
            }
            catch (UnauthorizedAccessException) { throw; }
            catch { /* 设备不支持 SetOSD，退回改设备名称 */ }
            await SoapAsync(baseUrl + "/onvif/device_service", "<tds:SetScopes><tds:Scopes>onvif://www.onvif.org/name/" + Uri.EscapeDataString(name) + "</tds:Scopes></tds:SetScopes>");
            return "该设备不支持改画面文字，已改 ONVIF 设备名称";
        }

        private async Task RenameChannelAsync()
        {
            string name = txtChannel.Text.Trim();
            if (name.Length == 0) { MessageBox.Show("通道名称不能为空。", "提示"); return; }
            if (name.Length > 32) { MessageBox.Show("通道名称太长(最多 32 个字符)。", "提示"); return; }
            btnRename.Enabled = false; SetStatus("正在修改通道名称...", Color.DimGray);
            try
            {
                string tip;
                if (mode == "ONVIF") tip = await RenameOnvifAsync(name);
                else if (mode == "HIK")
                {
                    string x = await HttpGetAsync("/ISAPI/System/Video/inputs/channels/1");
                    if (!Regex.IsMatch(x, "<name>[^<]*</name>")) throw new Exception("设备没有返回通道名称字段");
                    x = new Regex("(<name>)[^<]*(</name>)").Replace(x, m => m.Groups[1].Value + SecurityElement.Escape(name) + m.Groups[2].Value, 1);
                    await HttpPutXmlAsync("/ISAPI/System/Video/inputs/channels/1", x);
                    tip = "通道名称已修改";
                }
                else
                {
                    string r = await HttpGetAsync("/cgi-bin/configManager.cgi?action=setConfig&ChannelTitle[0].Name=" + Uri.EscapeDataString(name));
                    if (r.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("设备返回: " + Shorten(r));
                    tip = "通道名称已修改";
                }
                SetStatus(tip, Color.DarkGreen);
                if (vlcPlayer == null) await RefreshFrameAsync();
            }
            catch (UnauthorizedAccessException) { SetStatus("没有权限修改通道名称", Color.Firebrick); }
            catch (Exception ex) { SetStatus("修改通道名称失败: " + Shorten(ex.Message), Color.Firebrick); }
            finally { btnRename.Enabled = true; }
        }
        #endregion

        #region ---- 预览 / 应用 / 重启 ----
        private async Task RefreshFrameAsync()
        {
            if (busyFrame || mode == null || string.IsNullOrEmpty(snapshotUrl)) return;
            busyFrame = true;
            try
            {
                byte[] data = await HttpGetBytesAsync(snapshotUrl);
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms))
                {
                    var old = pic.Image;
                    pic.Image = new Bitmap(img);
                    if (old != null) old.Dispose();
                }
            }
            catch (Exception ex) { SetStatus("预览失败: " + Shorten(ex.Message), Color.Firebrick); }
            finally { busyFrame = false; }
        }

        private static bool ValidIp(string s) { IPAddress a; return IPAddress.TryParse(s, out a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && s.Split('.').Length == 4; }
        private static int MaskToPrefix(string mask) { int n = 0; foreach (var p in mask.Split('.')) { int b = int.Parse(p); while (b > 0) { n += b & 1; b >>= 1; } } return n; }
        private static string PrefixToMask(int prefix) { uint m = prefix <= 0 ? 0u : 0xFFFFFFFFu << (32 - Math.Min(prefix, 32)); return $"{m >> 24}.{(m >> 16) & 255}.{(m >> 8) & 255}.{m & 255}"; }

        private async Task ApplyAsync()
        {
            string nip = txtNewIp.Text.Trim(), mask = txtMask.Text.Trim(), gw = txtGw.Text.Trim(), dns = txtDns.Text.Trim();
            if (chkDhcp.Checked) { await ApplyDhcpAsync(); return; }
            if (!ValidIp(nip) || !ValidIp(mask) || (gw.Length > 0 && !ValidIp(gw)) || (dns.Length > 0 && !ValidIp(dns)))
            { MessageBox.Show("IP / 掩码 / 网关 / DNS 格式不正确（网关、DNS 可留空）。", "提示"); return; }
            if (MessageBox.Show($"确定把设备 {ip} 改为静态地址：\r\nIP: {nip}\r\n掩码: {mask}\r\n网关: {(gw.Length > 0 ? gw : "(不改)")}\r\nDNS: {(dns.Length > 0 ? dns : "(不改)")}\r\n\r\n应用后设备会断开连接。", "确认修改", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

            timer.Stop(); btnApply.Enabled = false; SetStatus("正在应用...", Color.DimGray);
            try
            {
                if (mode == "ONVIF") await ApplyOnvifAsync(nip, mask, gw, dns);
                else if (mode == "HIK") await ApplyHikAsync(nip, mask, gw, dns);
                else await ApplyDahuaAsync(nip, mask, gw, dns);
                SetStatus("已提交修改", Color.DarkGreen);
                MessageBox.Show("修改已提交。设备可能需要几秒到一分钟生效；IP 变更后请回到扫描页重新扫描。", "完成");
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is HttpRequestException || ex is IOException)
            {
                // 设备改 IP 后会立刻断开连接，这里的网络异常大概率代表已生效
                SetStatus("已提交(设备已断开，可能已生效)", Color.DarkGreen);
                MessageBox.Show("请求已发出，设备连接已断开，通常说明新 IP 已生效。请回到扫描页重新扫描确认。", "完成");
            }
            catch (UnauthorizedAccessException) { SetStatus("没有权限修改网络设置", Color.Firebrick); }
            catch (Exception ex) { SetStatus("修改失败: " + Shorten(ex.Message), Color.Firebrick); }
            finally { btnApply.Enabled = true; }
        }

        private async Task ApplyDhcpAsync()
        {
            if (MessageBox.Show($"确定把设备 {ip} 改为自动获取 IP(DHCP)吗？\r\n应用后设备会断开，新地址需要到路由器里查看或重新扫描。", "确认修改", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            timer.Stop(); btnApply.Enabled = false; SetStatus("正在应用...", Color.DimGray);
            try
            {
                if (mode == "ONVIF")
                {
                    if (string.IsNullOrEmpty(ifaceToken)) throw new Exception("没有读到网卡标识");
                    await SoapAsync(baseUrl + "/onvif/device_service", "<tds:SetNetworkInterfaces><tds:InterfaceToken>" + ifaceToken + "</tds:InterfaceToken><tds:NetworkInterface><tt:Enabled>true</tt:Enabled><tt:IPv4><tt:Enabled>true</tt:Enabled><tt:DHCP>true</tt:DHCP></tt:IPv4></tds:NetworkInterface></tds:SetNetworkInterfaces>");
                }
                else if (mode == "HIK")
                {
                    string x = await HttpGetAsync("/ISAPI/System/Network/interfaces/1");
                    x = Regex.Replace(x, @"(<addressingType>)[^<]*(</addressingType>)", "${1}dynamic${2}", RegexOptions.IgnoreCase);
                    await HttpPutXmlAsync("/ISAPI/System/Network/interfaces/1", x);
                }
                else
                {
                    string r = await HttpGetAsync("/cgi-bin/configManager.cgi?action=setConfig&Network.eth0.DhcpEnable=true");
                    if (r.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("设备返回: " + Shorten(r));
                }
                SetStatus("已提交 DHCP 设置", Color.DarkGreen);
                MessageBox.Show("已提交。设备会重新获取地址，请到路由器查看新 IP 或回到扫描页重新扫描。", "完成");
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is HttpRequestException || ex is IOException)
            { SetStatus("已提交(设备已断开，可能已生效)", Color.DarkGreen); }
            catch (UnauthorizedAccessException) { SetStatus("没有权限修改网络设置", Color.Firebrick); }
            catch (Exception ex) { SetStatus("修改失败: " + Shorten(ex.Message), Color.Firebrick); }
            finally { btnApply.Enabled = true; }
        }

        private async Task RebootAsync()
        {
            if (MessageBox.Show($"确定要重启设备 {ip} 吗？重启期间画面会中断。", "确认重启", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            timer.Stop();
            try
            {
                if (mode == "ONVIF") await SoapAsync(baseUrl + "/onvif/device_service", "<tds:SystemReboot/>");
                else if (mode == "HIK") await HttpPutXmlAsync("/ISAPI/System/reboot", "");
                else await HttpGetAsync("/cgi-bin/magicBox.cgi?action=reboot");
                SetStatus("已发送重启指令", Color.DarkGreen);
            }
            catch (Exception ex) { SetStatus("重启失败: " + Shorten(ex.Message), Color.Firebrick); }
        }
        #endregion

        #region ---- 扩展功能：时间 / 密码 / OSD / 码流 / 云台 / 抓拍 ----
        private readonly List<Control> loginControls = new List<Control>();
        private CheckBox chkDhcp, chkOsdName, chkOsdTime;
        private Button btnSnap, btnOsdApply, btnStreamApply, btnSyncTime, btnReadTime, btnPwd, btnCopyInfo, btnOpenWeb, btnCopyRtspAuth;
        private TextBox txtEnc, txtRes, txtNewPwd, txtNewPwd2;
        private NumericUpDown numFps, numBr;
        private Label lblDevTime;
        private string ptzUrl, lastPtz;
        private const string NsPtz = "http://www.onvif.org/ver20/ptz/wsdl";

        private void UpdateNetFields()
        {
            bool st = !chkDhcp.Checked && mode != null;
            txtNewIp.Enabled = txtMask.Enabled = txtGw.Enabled = txtDns.Enabled = st;
        }

        /// <summary>登录成功后读取时间、OSD、码流、DHCP 状态；任何一项失败都不影响其它功能。</summary>
        private async Task LoadExtrasAsync()
        {
            ptzUrl = null; lastPtz = null;
            txtEnc.Text = txtRes.Text = ""; chkOsdName.Checked = chkOsdTime.Checked = false;
            try { await ReadTimeAsync(); } catch (UnauthorizedAccessException) { } catch { lblDevTime.Text = "设备时间: 读取失败"; }
            try { await ReadOsdAsync(); } catch { }
            try { await ReadStreamAsync(); } catch { }
            if (mode == "ONVIF")
            {
                try
                {
                    string caps = await SoapAsync(baseUrl + "/onvif/device_service", "<tds:GetCapabilities><tds:Category>All</tds:Category></tds:GetCapabilities>");
                    string p = Rx(caps, @"<(?:\w+:)?PTZ>\s*<(?:\w+:)?XAddr>([^<]+)<");
                    if (p.Length > 0) ptzUrl = FixHost(p);
                }
                catch { }
            }
            UpdateNetFields();
        }

        // ---------- 抓拍 ----------
        private async Task SnapshotAsync()
        {
            using (var dlg = new SaveFileDialog { Filter = "JPG 图片|*.jpg", FileName = $"{ip}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (!string.IsNullOrEmpty(snapshotUrl))
                    {
                        File.WriteAllBytes(dlg.FileName, await HttpGetBytesAsync(snapshotUrl));
                    }
                    else if (vlcPlayer != null && vlcPlayer.TakeSnapshot(0, dlg.FileName, 0, 0)) { }
                    else throw new Exception("设备没有快照地址，视频也未播放");
                    SetStatus("已保存: " + dlg.FileName, Color.DarkGreen);
                }
                catch (Exception ex) { SetStatus("抓拍失败: " + Shorten(ex.Message), Color.Firebrick); }
            }
        }

        // ---------- 设备时间 ----------
        private async Task ReadTimeAsync()
        {
            string t = "";
            if (mode == "ONVIF")
            {
                string x = await SoapAsync(baseUrl + "/onvif/device_service", "<tds:GetSystemDateAndTime/>", false);
                string utc = Rx(x, @"UTCDateTime>(.*?)</[^>]*UTCDateTime>"); if (utc.Length == 0) utc = x;
                var d = new DateTime(int.Parse(Rx(utc, @"Year>(\d+)<")), int.Parse(Rx(utc, @"Month>(\d+)<")), int.Parse(Rx(utc, @"Day>(\d+)<")),
                    int.Parse(Rx(utc, @"Hour>(\d+)<")), int.Parse(Rx(utc, @"Minute>(\d+)<")), int.Parse(Rx(utc, @"Second>(\d+)<")), DateTimeKind.Utc);
                t = d.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " (按本机时区换算)";
            }
            else if (mode == "HIK") t = Rx(await HttpGetAsync("/ISAPI/System/time"), @"localTime>([^<]*)<").Replace("T", " ");
            else t = Rx(await HttpGetAsync("/cgi-bin/global.cgi?action=getCurrentTime"), @"result=([^\r\n]*)");
            lblDevTime.Text = "设备时间: " + t + "\r\n本机时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>只做对时本身(不碰界面)，失败抛异常。</summary>
        private async Task DoSyncTimeAsync()
        {
            var now = DateTime.Now; var utc = DateTime.UtcNow;
            if (mode == "ONVIF")
                await SoapAsync(baseUrl + "/onvif/device_service", "<tds:SetSystemDateAndTime><tds:DateTimeType>Manual</tds:DateTimeType><tds:DaylightSavings>false</tds:DaylightSavings><tds:UTCDateTime><tt:Time><tt:Hour>" + utc.Hour + "</tt:Hour><tt:Minute>" + utc.Minute + "</tt:Minute><tt:Second>" + utc.Second + "</tt:Second></tt:Time><tt:Date><tt:Year>" + utc.Year + "</tt:Year><tt:Month>" + utc.Month + "</tt:Month><tt:Day>" + utc.Day + "</tt:Day></tt:Date></tds:UTCDateTime></tds:SetSystemDateAndTime>");
            else if (mode == "HIK")
            {
                string x = await HttpGetAsync("/ISAPI/System/time");
                x = Regex.Replace(x, @"(<timeMode>)[^<]*(</timeMode>)", "${1}manual${2}", RegexOptions.IgnoreCase);
                string lt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                x = Regex.Replace(x, @"(<localTime>)[^<]*(</localTime>)", "${1}" + lt + "${2}", RegexOptions.IgnoreCase);
                await HttpPutXmlAsync("/ISAPI/System/time", x);
            }
            else
            {
                string r = await HttpGetAsync("/cgi-bin/global.cgi?action=setCurrentTime&time=" + Uri.EscapeDataString(now.ToString("yyyy-MM-dd HH:mm:ss")));
                if (r.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("设备返回: " + Shorten(r));
            }
        }

        /// <summary>批量对时用：需先 HeadlessLoginAsync 成功。返回空串表示成功，否则是失败原因。</summary>
        public async Task<string> HeadlessSyncTimeAsync()
        {
            try { await DoSyncTimeAsync(); return ""; }
            catch (UnauthorizedAccessException) { return "没有权限修改时间"; }
            catch (Exception ex) { return Shorten(ex.Message); }
        }

        private async Task SyncTimeAsync()
        {
            btnSyncTime.Enabled = false; SetStatus("正在同步时间...", Color.DimGray);
            try
            {
                await DoSyncTimeAsync();
                SetStatus("时间已同步", Color.DarkGreen);
                await Task.Delay(500); await ReadTimeAsync();
            }
            catch (UnauthorizedAccessException) { SetStatus("没有权限修改时间", Color.Firebrick); }
            catch (Exception ex) { SetStatus("同步时间失败: " + Shorten(ex.Message), Color.Firebrick); }
            finally { btnSyncTime.Enabled = true; }
        }

        // ---------- 修改密码 ----------
        private async Task ChangePasswordAsync()
        {
            string np = txtNewPwd.Text;
            if (np.Length == 0) { MessageBox.Show("请输入新密码。", "提示"); return; }
            if (np != txtNewPwd2.Text) { MessageBox.Show("两次输入的新密码不一致。", "提示"); return; }
            if (np == pass) { MessageBox.Show("新密码与当前密码相同。", "提示"); return; }
            if (MessageBox.Show($"确定把设备 {ip} 上账号 {user} 的密码改掉吗？\r\n请先记牢新密码，忘记后只能硬复位。", "确认改密码", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            btnPwd.Enabled = false; SetStatus("正在修改密码...", Color.DimGray);
            try
            {
                if (mode == "ONVIF")
                {
                    string dev = baseUrl + "/onvif/device_service", level = "Administrator";
                    try
                    {
                        string us = await SoapAsync(dev, "<tds:GetUsers/>");
                        var m = Regex.Match(us, @"<(?:\w+:)?User>.*?</(?:\w+:)?User>", RegexOptions.Singleline);
                        foreach (Match mm in Regex.Matches(us, @"<(?:\w+:)?User>.*?</(?:\w+:)?User>", RegexOptions.Singleline))
                            if (Rx(mm.Value, @"Username>([^<]*)<") == user) { string l = Rx(mm.Value, @"UserLevel>([^<]*)<"); if (l.Length > 0) level = l; }
                    }
                    catch (UnauthorizedAccessException) { throw; } catch { }
                    await SoapAsync(dev, "<tds:SetUser><tds:User><tt:Username>" + SecurityElement.Escape(user) + "</tt:Username><tt:Password>" + SecurityElement.Escape(np) + "</tt:Password><tt:UserLevel>" + level + "</tt:UserLevel></tds:User></tds:SetUser>");
                }
                else if (mode == "HIK")
                {
                    string us = await HttpGetAsync("/ISAPI/Security/users");
                    string id = "1";
                    foreach (Match mm in Regex.Matches(us, @"<User>.*?</User>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                        if (Rx(mm.Value, @"userName>([^<]*)<") == user) { id = Rx(mm.Value, @"<id>([^<]*)<"); break; }
                    await HttpPutXmlAsync("/ISAPI/Security/users/" + id, "<User><id>" + id + "</id><userName>" + SecurityElement.Escape(user) + "</userName><password>" + SecurityElement.Escape(np) + "</password></User>");
                }
                else
                {
                    string r = await HttpGetAsync("/cgi-bin/userManager.cgi?action=modifyPassword&name=" + Uri.EscapeDataString(user) + "&pwd=" + Uri.EscapeDataString(np) + "&pwdOld=" + Uri.EscapeDataString(pass ?? ""));
                    if (r.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("设备返回: " + Shorten(r));
                }
                txtNewPwd.Text = txtNewPwd2.Text = "";
                txtPass.Text = np;
                SetStatus("密码已修改，正在用新密码重新登录...", Color.DarkGreen);
                await Task.Delay(800);
                await LoginAsync();
            }
            catch (UnauthorizedAccessException) { SetStatus("没有权限修改密码", Color.Firebrick); }
            catch (Exception ex) { SetStatus("修改密码失败: " + Shorten(ex.Message), Color.Firebrick); }
            finally { btnPwd.Enabled = mode != null; }
        }

        // ---------- OSD ----------
        private async Task ReadOsdAsync()
        {
            if (mode == "HIK")
            {
                string x = await HttpGetAsync("/ISAPI/System/Video/inputs/channels/1/overlays");
                chkOsdName.Checked = Rx(RxCs(x, @"<channelNameOverlay.*?</channelNameOverlay>"), @"enabled>([^<]*)<").ToLower() == "true";
                chkOsdTime.Checked = Rx(RxCs(x, @"<DateTimeOverlay.*?</DateTimeOverlay>"), @"enabled>([^<]*)<").ToLower() == "true";
            }
            else if (mode == "DAHUA")
            {
                string x = await HttpGetAsync("/cgi-bin/configManager.cgi?action=getConfig&name=VideoWidget");
                chkOsdName.Checked = Rx(x, @"VideoWidget\[0\]\.ChannelTitle\.EncodeBlend=([^\r\n]*)").ToLower() == "true";
                chkOsdTime.Checked = Rx(x, @"VideoWidget\[0\]\.TimeTitle\.EncodeBlend=([^\r\n]*)").ToLower() == "true";
            }
        }

        private async Task ApplyOsdAsync()
        {
            if (mode == "ONVIF") { SetStatus("ONVIF 设备暂不支持开关 OSD", Color.Firebrick); return; }
            btnOsdApply.Enabled = false; SetStatus("正在应用 OSD...", Color.DimGray);
            try
            {
                string n = chkOsdName.Checked ? "true" : "false", t = chkOsdTime.Checked ? "true" : "false";
                if (mode == "HIK")
                {
                    string p = "/ISAPI/System/Video/inputs/channels/1/overlays";
                    string x = await HttpGetAsync(p);
                    x = Regex.Replace(x, @"(<channelNameOverlay.*?<enabled>)[^<]*(</enabled>)", "${1}" + n + "${2}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    x = Regex.Replace(x, @"(<DateTimeOverlay.*?<enabled>)[^<]*(</enabled>)", "${1}" + t + "${2}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    await HttpPutXmlAsync(p, x);
                }
                else
                {
                    string r = await HttpGetAsync("/cgi-bin/configManager.cgi?action=setConfig&VideoWidget[0].ChannelTitle.EncodeBlend=" + n + "&VideoWidget[0].TimeTitle.EncodeBlend=" + t);
                    if (r.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("设备返回: " + Shorten(r));
                }
                SetStatus("OSD 设置已应用", Color.DarkGreen);
            }
            catch (UnauthorizedAccessException) { SetStatus("没有权限修改 OSD", Color.Firebrick); }
            catch (Exception ex) { SetStatus("应用 OSD 失败: " + Shorten(ex.Message), Color.Firebrick); }
            finally { btnOsdApply.Enabled = true; }
        }

        // ---------- 码流 ----------
        private async Task ReadStreamAsync()
        {
            string enc = "", w = "", h = "", fps = "", br = "";
            if (mode == "ONVIF")
            {
                string prof = await SoapAsync(mediaUrl, "<trt:GetProfiles/>");
                string v = RxCs(prof, @"<(?:\w+:)?VideoEncoderConfiguration\b.*?</(?:\w+:)?VideoEncoderConfiguration>");
                enc = Rx(v, @"Encoding>([^<]*)<"); w = Rx(v, @"Width>(\d+)<"); h = Rx(v, @"Height>(\d+)<");
                fps = Rx(v, @"FrameRateLimit>(\d+)<"); br = Rx(v, @"BitrateLimit>(\d+)<");
            }
            else if (mode == "HIK")
            {
                string x = await HttpGetAsync("/ISAPI/Streaming/channels/101");
                enc = Rx(x, @"videoCodecType>([^<]*)<"); w = Rx(x, @"videoResolutionWidth>(\d+)<"); h = Rx(x, @"videoResolutionHeight>(\d+)<");
                int f; if (int.TryParse(Rx(x, @"maxFrameRate>(\d+)<"), out f)) fps = (f >= 100 ? f / 100 : f).ToString();
                br = Rx(x, @"constantBitRate>(\d+)<"); if (br.Length == 0) br = Rx(x, @"vbrUpperCap>(\d+)<");
            }
            else
            {
                string x = await HttpGetAsync("/cgi-bin/configManager.cgi?action=getConfig&name=Encode");
                enc = Rx(x, @"Encode\[0\]\.MainFormat\[0\]\.Video\.Compression=([^\r\n]*)"); w = Rx(x, @"MainFormat\[0\]\.Video\.Width=(\d+)"); h = Rx(x, @"MainFormat\[0\]\.Video\.Height=(\d+)");
                fps = Rx(x, @"MainFormat\[0\]\.Video\.FPS=(\d+)"); br = Rx(x, @"MainFormat\[0\]\.Video\.BitRate=(\d+)");
            }
            txtEnc.Text = enc; txtRes.Text = (w.Length > 0 ? w + " x " + h : "");
            int vv;
            if (int.TryParse(fps, out vv) && vv >= 1 && vv <= 60) numFps.Value = vv;
            if (int.TryParse(br, out vv) && vv >= 32 && vv <= 32768) numBr.Value = vv;
            bool canSet = mode != "ONVIF";
            numFps.Enabled = numBr.Enabled = btnStreamApply.Enabled = canSet;
        }

        private async Task ApplyStreamAsync()
        {
            if (mode == "ONVIF") { SetStatus("ONVIF 设备暂不支持改码流", Color.Firebrick); return; }
            btnStreamApply.Enabled = false; SetStatus("正在应用码流设置...", Color.DimGray);
            try
            {
                int fps = (int)numFps.Value, br = (int)numBr.Value;
                if (mode == "HIK")
                {
                    string p = "/ISAPI/Streaming/channels/101";
                    string x = await HttpGetAsync(p);
                    x = Regex.Replace(x, @"(<maxFrameRate>)\d+(</maxFrameRate>)", "${1}" + fps * 100 + "${2}", RegexOptions.IgnoreCase);
                    x = Regex.Replace(x, @"(<constantBitRate>)\d+(</constantBitRate>)", "${1}" + br + "${2}", RegexOptions.IgnoreCase);
                    x = Regex.Replace(x, @"(<vbrUpperCap>)\d+(</vbrUpperCap>)", "${1}" + br + "${2}", RegexOptions.IgnoreCase);
                    await HttpPutXmlAsync(p, x);
                }
                else
                {
                    string r = await HttpGetAsync("/cgi-bin/configManager.cgi?action=setConfig&Encode[0].MainFormat[0].Video.FPS=" + fps + "&Encode[0].MainFormat[0].Video.BitRate=" + br);
                    if (r.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("设备返回: " + Shorten(r));
                }
                SetStatus("码流设置已应用(画面可能中断几秒)", Color.DarkGreen);
            }
            catch (UnauthorizedAccessException) { SetStatus("没有权限修改码流", Color.Firebrick); }
            catch (Exception ex) { SetStatus("应用码流失败: " + Shorten(ex.Message), Color.Firebrick); }
            finally { btnStreamApply.Enabled = true; }
        }

        // ---------- 云台 / 变焦 ----------
        private async Task PtzSafeAsync(string dir, bool start)
        {
            if (mode == null) return;
            try { await PtzAsync(dir, start); }
            catch (Exception ex) { SetStatus("云台操作失败(设备可能没有云台): " + Shorten(ex.Message), Color.Firebrick); }
        }

        private async Task PtzAsync(string dir, bool start)
        {
            bool stop = !start || dir == "S";
            if (!stop) lastPtz = dir;
            string d = stop && dir != "S" ? dir : (dir == "S" ? lastPtz : dir);
            if (mode == "ONVIF")
            {
                if (string.IsNullOrEmpty(ptzUrl) || string.IsNullOrEmpty(profileToken)) throw new Exception("设备没有 PTZ 服务");
                if (stop) { await SoapAsync(ptzUrl, "<tptz:Stop xmlns:tptz=\"" + NsPtz + "\"><tptz:ProfileToken>" + profileToken + "</tptz:ProfileToken><tptz:PanTilt>true</tptz:PanTilt><tptz:Zoom>true</tptz:Zoom></tptz:Stop>"); return; }
                double x = 0, y = 0, z = 0;
                switch (dir) { case "U": y = 0.5; break; case "D": y = -0.5; break; case "L": x = -0.5; break; case "R": x = 0.5; break; case "I": z = 0.5; break; case "O": z = -0.5; break; }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                await SoapAsync(ptzUrl, "<tptz:ContinuousMove xmlns:tptz=\"" + NsPtz + "\"><tptz:ProfileToken>" + profileToken + "</tptz:ProfileToken><tptz:Velocity><tt:PanTilt x=\"" + x.ToString(ci) + "\" y=\"" + y.ToString(ci) + "\"/><tt:Zoom x=\"" + z.ToString(ci) + "\"/></tptz:Velocity></tptz:ContinuousMove>");
            }
            else if (mode == "HIK")
            {
                int pan = 0, tilt = 0, zoom = 0;
                if (!stop) switch (dir) { case "U": tilt = 60; break; case "D": tilt = -60; break; case "L": pan = -60; break; case "R": pan = 60; break; case "I": zoom = 60; break; case "O": zoom = -60; break; }
                await HttpPutXmlAsync("/ISAPI/PTZCtrl/channels/1/continuous", "<PTZData><pan>" + pan + "</pan><tilt>" + tilt + "</tilt><zoom>" + zoom + "</zoom></PTZData>");
            }
            else
            {
                string code = null;
                switch (d) { case "U": code = "Up"; break; case "D": code = "Down"; break; case "L": code = "Left"; break; case "R": code = "Right"; break; case "I": code = "ZoomTele"; break; case "O": code = "ZoomWide"; break; }
                if (code == null) return;
                await HttpGetAsync("/cgi-bin/ptz.cgi?action=" + (stop ? "stop" : "start") + "&channel=0&code=" + code + "&arg1=0&arg2=5&arg3=0");
            }
        }
        #endregion
    }
}
