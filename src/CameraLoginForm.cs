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
            lblStatus = new Label { Text = "请输入账号密码后登录", AutoSize = true, Margin = new Padding(0, 8, 0, 0), ForeColor = Color.DimGray, Font = fNormal };
            flow.Controls.Add(lblStatus);
            top.Controls.Add(flow);
            root.Controls.Add(top, 0, 0);

            // ---- 主区域：左 预览+信息，右 设置 ----
            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390F));
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

            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0) };
            main.Controls.Add(right, 1, 0);

            // 网络设置
            var net = new GroupBox { Text = " 网络设置(登录后可用) ", Width = 365, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = fBold, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(0, 0, 0, 10) };
            var nt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
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
            txtNewIp = addRow("IP 地址:");
            txtMask = addRow("子网掩码:");
            txtGw = addRow("默认网关:");
            txtDns = addRow("DNS:");
            nt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var tip = new Label { Text = "改 IP 会改为静态地址；应用后设备会断开，需要用新 IP 重新扫描/登录。请确认新 IP 不冲突。", AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 6), Font = fNormal };
            nt.Controls.Add(tip, 0, r); nt.SetColumnSpan(tip, 2); r++;
            nt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var nbtn = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
            btnApply = new Button { Text = "应用修改", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(100, 32), Enabled = false, Font = fBold, Margin = new Padding(0, 0, 10, 0) };
            btnReboot = new Button { Text = "重启设备", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(100, 32), Enabled = false, Font = fNormal, Margin = new Padding(0) };
            nbtn.Controls.Add(btnApply); nbtn.Controls.Add(btnReboot);
            nt.Controls.Add(nbtn, 0, r); nt.SetColumnSpan(nbtn, 2);
            net.Controls.Add(nt);
            right.Controls.Add(net);

            // 通道名称
            var ch = new GroupBox { Text = " 通道名称(画面上显示的名字) ", Width = 365, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = fBold, Padding = new Padding(8, 4, 8, 8), Margin = new Padding(0) };
            var ct = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            txtChannel = new TextBox { Width = 330, Enabled = false, Font = fNormal, Margin = new Padding(0, 4, 0, 6) };
            btnRename = new Button { Text = "修改通道名称", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(120, 32), Enabled = false, Font = fBold, Margin = new Padding(0, 0, 0, 6) };
            var chTip = new Label { Text = "海康/大华改通道名；ONVIF 设备改画面叠加文字，设备不支持时改设备名称。修改后画面标题会更新。", AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.DimGray, Font = fNormal };
            ct.Controls.Add(txtChannel); ct.Controls.Add(btnRename); ct.Controls.Add(chTip);
            ch.Controls.Add(ct);
            right.Controls.Add(ch);

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
            txtNewIp.Enabled = txtMask.Enabled = txtGw.Enabled = txtDns.Enabled = on;
            btnApply.Enabled = btnReboot.Enabled = chkLive.Enabled = btnRefresh.Enabled = on;
            txtChannel.Enabled = btnRename.Enabled = on;
            btnCopyRtsp.Enabled = on && !string.IsNullOrEmpty(rtspUrl);
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
    }
}
