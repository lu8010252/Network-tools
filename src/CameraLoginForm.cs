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

        private TextBox txtUser, txtPass, txtNewIp, txtMask, txtGw, txtDns, txtInfo;
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

        private static readonly Font fNormal = new Font("Microsoft YaHei", 9F);
        private static readonly Font fBold = new Font("Microsoft YaHei", 9F, FontStyle.Bold);

        public CameraLoginForm(string ip, string brand, int webPort, string openPorts)
        {
            this.ip = ip; this.brand = brand ?? "";
            BuildUi(webPort, openPorts);
        }

        #region ---- UI ----
        private void BuildUi(int webPort, string openPorts)
        {
            Text = $"设备登录 - {ip}  {brand}";
            Size = new Size(1000, 640);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;
            Font = fNormal;

            var top = new GroupBox { Text = " 登录 ", Location = new Point(12, 8), Size = new Size(960, 58), Font = fBold, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            top.Controls.Add(new Label { Text = "IP:", Location = new Point(12, 28), AutoSize = true, Font = fNormal });
            top.Controls.Add(new Label { Text = ip, Location = new Point(34, 28), AutoSize = true, Font = fBold });
            top.Controls.Add(new Label { Text = "端口:", Location = new Point(150, 28), AutoSize = true, Font = fNormal });
            cmbPort = new ComboBox { Location = new Point(190, 24), Width = 70, Font = fNormal };
            var ports = new List<string>();
            if (webPort > 0) ports.Add(webPort.ToString());
            foreach (var p in (openPorts ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n; if (int.TryParse(p.Trim(), out n) && new[] { 80, 81, 8080, 8081, 8088, 8888, 8899, 2020, 5000, 443, 8443 }.Contains(n) && !ports.Contains(n.ToString())) ports.Add(n.ToString());
            }
            if (ports.Count == 0) ports.Add("80");
            cmbPort.Items.AddRange(ports.ToArray());
            cmbPort.Text = ports[0];
            top.Controls.Add(cmbPort);
            top.Controls.Add(new Label { Text = "用户名:", Location = new Point(275, 28), AutoSize = true, Font = fNormal });
            txtUser = new TextBox { Location = new Point(325, 24), Width = 110, Text = "admin", Font = fNormal };
            top.Controls.Add(txtUser);
            top.Controls.Add(new Label { Text = "密码:", Location = new Point(445, 28), AutoSize = true, Font = fNormal });
            txtPass = new TextBox { Location = new Point(485, 24), Width = 130, UseSystemPasswordChar = true, Font = fNormal };
            top.Controls.Add(txtPass);
            btnLogin = new Button { Text = "登录", Location = new Point(630, 22), Size = new Size(80, 28), Font = fBold };
            top.Controls.Add(btnLogin);
            lblStatus = new Label { Text = "请输入账号密码后登录", Location = new Point(720, 28), AutoSize = true, ForeColor = Color.DimGray, Font = fNormal };
            top.Controls.Add(lblStatus);
            Controls.Add(top);

            pic = new PictureBox { Location = new Point(12, 74), Size = new Size(590, 400), BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right };
            Controls.Add(pic);
            chkLive = new CheckBox { Text = "实时刷新预览(约每秒1张快照)", Location = new Point(12, 482), AutoSize = true, Checked = true, Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            btnRefresh = new Button { Text = "刷新一张", Location = new Point(250, 478), Size = new Size(80, 26), Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            btnCopyRtsp = new Button { Text = "复制 RTSP 地址", Location = new Point(340, 478), Size = new Size(110, 26), Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            Controls.Add(chkLive); Controls.Add(btnRefresh); Controls.Add(btnCopyRtsp);

            txtInfo = new TextBox { Location = new Point(12, 512), Size = new Size(590, 82), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right };
            Controls.Add(txtInfo);

            var net = new GroupBox { Text = " 网络设置(登录后可用) ", Location = new Point(614, 74), Size = new Size(358, 270), Font = fBold, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            int y = 28;
            Func<string, TextBox> addRow = (label) =>
            {
                net.Controls.Add(new Label { Text = label, Location = new Point(14, y + 3), AutoSize = true, Font = fNormal });
                var tb = new TextBox { Location = new Point(100, y), Width = 235, Font = fNormal, Enabled = false };
                net.Controls.Add(tb); y += 34; return tb;
            };
            txtNewIp = addRow("IP 地址:");
            txtMask = addRow("子网掩码:");
            txtGw = addRow("默认网关:");
            txtDns = addRow("DNS:");
            net.Controls.Add(new Label { Text = "改 IP 会改为静态地址；应用后设备会断开，\r\n需要用新 IP 重新扫描/登录。请确认新 IP 不冲突。", Location = new Point(14, y), Size = new Size(330, 40), ForeColor = Color.DimGray, Font = fNormal });
            btnApply = new Button { Text = "应用修改", Location = new Point(14, y + 48), Size = new Size(110, 30), Enabled = false, Font = fBold };
            btnReboot = new Button { Text = "重启设备", Location = new Point(135, y + 48), Size = new Size(110, 30), Enabled = false, Font = fNormal };
            net.Controls.Add(btnApply); net.Controls.Add(btnReboot);
            Controls.Add(net);

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += async (s, e) => { if (chkLive.Checked) await RefreshFrameAsync(); };

            AcceptButton = btnLogin;
            btnLogin.Click += async (s, e) => await LoginAsync();
            btnRefresh.Click += async (s, e) => await RefreshFrameAsync();
            btnCopyRtsp.Click += (s, e) => { try { Clipboard.SetText(rtspUrl ?? ""); SetStatus("RTSP 地址已复制(不含密码，播放器里需填账号密码)", Color.DarkGreen); } catch { } };
            btnApply.Click += async (s, e) => await ApplyAsync();
            btnReboot.Click += async (s, e) => await RebootAsync();
            FormClosed += (s, e) => { timer.Stop(); timer.Dispose(); http?.Dispose(); if (pic.Image != null) pic.Image.Dispose(); pass = null; };
        }

        private void SetStatus(string text, Color color) { lblStatus.Text = text; lblStatus.ForeColor = color; }
        private void SetLoggedIn(bool on)
        {
            txtNewIp.Enabled = txtMask.Enabled = txtGw.Enabled = txtDns.Enabled = on;
            btnApply.Enabled = btnReboot.Enabled = chkLive.Enabled = btnRefresh.Enabled = on;
            btnCopyRtsp.Enabled = on && !string.IsNullOrEmpty(rtspUrl);
        }
        #endregion

        #region ---- 登录 ----
        private async Task LoginAsync()
        {
            user = txtUser.Text.Trim(); pass = txtPass.Text;
            if (user.Length == 0) { SetStatus("请输入用户名", Color.Firebrick); return; }

            btnLogin.Enabled = false; timer.Stop(); SetLoggedIn(false);
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
            await RefreshFrameAsync();
            timer.Start();
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
