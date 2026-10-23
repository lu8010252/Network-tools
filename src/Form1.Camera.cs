using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.Security.Principal;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Net.Http;
using Microsoft.Win32;
#nullable disable

namespace NeworkTool
{
    public partial class Form1
    {
        #region ====== 5.5 局域网摄像头/监控设备扫描 ======
        /// <summary>
        /// 摄像头/NVR/路由器类设备识别信息。WebPort 是双击后要打开的 Web 管理页面端口。
        /// </summary>
        private class CameraDeviceInfo
        {
            public string Ip;
            public string Brand;
            public string Mac;
            public int WebPort;
            public string OpenPorts;
            public string Basis;
        }

        /// <summary>
        /// 专门用来抓取摄像头/路由器 Web 管理页面指纹的 HttpClient：
        /// 这类设备大部分用的是自签名证书或者干脆没证书，SSL 校验默认打开的话 https 探测会直接失败，
        /// 所以这里显式关闭证书校验；超时给得比较短(2秒)，避免个别无响应的设备拖慢整体扫描进度。
        /// 跟查公网IP用的 publicIpHttpClient 分开是因为要求不一样（一个要禁用证书校验，一个不用）。
        /// </summary>
        private static readonly HttpClient cameraHttpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true,
            AllowAutoRedirect = false
        })
        { Timeout = TimeSpan.FromSeconds(2) };

        // 【品牌识别规则】关键词匹配 Web 管理页面返回的 Server 响应头 / 认证 Realm / 页面正文，
        // 命中即视为"确认"，可信度最高。海康/大华/天地伟业/TP-LINK/水星这几个品牌的设备
        // 绝大多数会在登录页或响应头里带上品牌相关的英文/中文字样，命中率比较高。
        private static readonly (string keyword, string brand)[] cameraBrandKeywords = new[]
        {
            // 海康：登录页 /doc/page/login.asp、ISAPI、App-webs、DNVRS-Webs 都是海康特征
            ("hikvision", "海康威视 (Hikvision)"),
            ("海康", "海康威视 (Hikvision)"),
            ("dnvrs-webs", "海康威视 (Hikvision)"),
            ("app-webs", "海康威视 (Hikvision)"),
            ("/doc/page/login.asp", "海康威视 (Hikvision)"),
            ("/isapi/", "海康威视 (Hikvision)"),
            ("dahua", "大华 (Dahua)"),
            ("大华", "大华 (Dahua)"),
            ("dvrdvs-webs", "大华 (Dahua)"),
            ("rpc2_login", "大华 (Dahua)"),
            // 天地伟业 Tiandy（注意：TVT 是同为，不是天地伟业）
            ("tiandy", "天地伟业 (Tiandy)"),
            ("天地伟业", "天地伟业 (Tiandy)"),
            ("tdwy", "天地伟业 (Tiandy)"),
            ("uniview", "宇视 (Uniview)"),
            ("宇视", "宇视 (Uniview)"),
            ("ezviz", "萤石 (EZVIZ)"),
            ("萤石", "萤石 (EZVIZ)"),
            ("tvt", "同为 (TVT)"),
            ("同为", "同为 (TVT)"),
            ("xmeye", "雄迈(Xiongmai)系 / 杂牌OEM"),
            ("netsurveillance", "雄迈(Xiongmai)系 / 杂牌OEM"),
            ("uc-httpd", "雄迈(Xiongmai)系 / 杂牌OEM"),
            ("jaws/1.0", "杂牌OEM DVR/NVR (JAWS)"),
            ("goahead-webs", "杂牌OEM摄像头 (GoAhead)"),
            ("ip camera", "网络摄像头 (未识别品牌)"),
            ("ipcamera", "网络摄像头 (未识别品牌)"),
            ("network camera", "网络摄像头 (未识别品牌)"),
            ("网络摄像机", "网络摄像头 (未识别品牌)"),
            ("网络硬盘录像机", "网络录像机 NVR (未识别品牌)"),
            ("webcamera", "网络摄像头 (未识别品牌)"),
            ("rtsp server", "网络摄像头 (RTSP设备)"),
            ("tp-link", "TP-LINK"),
            ("tplink", "TP-LINK"),
            ("melogin", "水星 (Mercury)"),
            ("mercurycom", "水星 (Mercury)"),
            ("水星", "水星 (Mercury)"),

            ("onvif", "ONVIF 通用网络摄像头"),
        };

        private void BuildTabCameraLayout()
        {
            GroupBox groupCtrl = new GroupBox { Text = " 📷 局域网摄像头 / 监控设备扫描 ", Location = new Point(15, 15), Size = new Size(880, 100), Font = fontBold, ForeColor = colorPrimary };
            tabCamera.Controls.Add(groupCtrl);
            AutoStretch(tabCamera, groupCtrl);

            Label lblDesc = new Label
            {
                Text = "自动扫描当前网段，通过 Web 管理页面指纹 + 常见私有协议端口特征，识别海康、大华、天地伟业、宇视、同为、雄迈系杂牌等摄像头/NVR 设备（含 ONVIF / RTSP 探测）。扫描完成后，双击列表中的一行即可登录设备（预览画面、修改 IP 等），右键可用浏览器打开 Web 管理页面。",
                Location = new Point(15, 22),
                Size = new Size(850, 40),
                Font = fontNormal,
                ForeColor = Color.DimGray
            };
            groupCtrl.Controls.Add(lblDesc);
            AutoStretch(groupCtrl, lblDesc);

            btnStartCameraScan = new Button { Text = "🔍 开始扫描摄像头", Location = new Point(15, 65), Size = new Size(160, 32), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite, Font = fontBold };
            btnStartCameraScan.FlatAppearance.BorderSize = 0;
            btnStopCameraScan = new Button { Text = "🔲 停止", Location = new Point(185, 65), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false, Font = fontNormal };
            btnStopCameraScan.FlatAppearance.BorderColor = colorBorder;
            progressBarCamera = new ProgressBar { Location = new Point(280, 69), Size = new Size(585, 23), Style = ProgressBarStyle.Continuous };
            groupCtrl.Controls.AddRange(new Control[] { btnStartCameraScan, btnStopCameraScan, progressBarCamera });
            AutoStretch(groupCtrl, progressBarCamera);

            listViewCamera = new ListView { Location = new Point(15, 125), Size = new Size(880, 400), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Consolas", 9.5F) };
            listViewCamera.Columns.AddRange(new ColumnHeader[]
            {
                new ColumnHeader { Text = "设备品牌", Width = 170 },
                new ColumnHeader { Text = "IP 地址", Width = 130 },
                new ColumnHeader { Text = "开放端口", Width = 110 },
                new ColumnHeader { Text = "MAC 地址", Width = 150 },
                new ColumnHeader { Text = "识别依据 / 标识信息", Width = 300 },
            });
            tabCamera.Controls.Add(listViewCamera);
            AutoFillLastColumn(listViewCamera, 200);

            lblCameraFooter = new Label { Text = "就绪 | 点击「开始扫描摄像头」定位局域网内的监控设备...", Location = new Point(15, 530), Size = new Size(880, 26), BackColor = colorPrimaryLt, TextAlign = ContentAlignment.MiddleLeft };
            tabCamera.Controls.Add(lblCameraFooter);
            AutoStretch(tabCamera, lblCameraFooter);
            AutoStickBottom(tabCamera, lblCameraFooter);
            // listViewCamera 高度要留出底部 footer 的空间，所以底边距用 footer高度+15 的间隙。
            AutoStretch(tabCamera, listViewCamera, width: true, height: true, rightMargin: 15, bottomMargin: lblCameraFooter.Height + 20);

            // 双击打开登录窗口(预览画面 / 查看设备信息 / 修改 IP 等)
            listViewCamera.MouseDoubleClick += (s, e) =>
            {
                if (listViewCamera.SelectedItems.Count == 0) return;
                if (listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) OpenCameraLogin(info);
            };

            // 右键菜单，跟其它页面（软件卸载/共享管理）的操作习惯保持一致
            var ctxMenu = new ContextMenuStrip();
            var ctxLogin = new ToolStripMenuItem("登录设备 / 预览 / 修改 IP...");
            var ctxOpenWeb = new ToolStripMenuItem("在浏览器中打开 Web 管理页面");
            var ctxCopyIp = new ToolStripMenuItem("复制 IP 地址");
            ctxMenu.Items.AddRange(new ToolStripItem[] { ctxLogin, ctxOpenWeb, ctxCopyIp });
            listViewCamera.ContextMenuStrip = ctxMenu;
            listViewCamera.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var hit = listViewCamera.HitTest(e.Location);
                if (hit.Item != null) hit.Item.Selected = true;
            };
            ctxLogin.Click += (s, e) => { if (listViewCamera.SelectedItems.Count > 0 && listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) OpenCameraLogin(info); };
            ctxOpenWeb.Click += (s, e) => { if (listViewCamera.SelectedItems.Count > 0 && listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) OpenCameraWebPage(info); };
            ctxCopyIp.Click += (s, e) => { if (listViewCamera.SelectedItems.Count > 0 && listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) { try { Clipboard.SetText(info.Ip); } catch { } } };

            btnStartCameraScan.Click += async (s, e) => { await StartCameraScanAsync(); };
            btnStopCameraScan.Click += (s, e) => { ctsCamera?.Cancel(); };
        }

        private void OpenCameraLogin(CameraDeviceInfo info)
        {
            using (var dlg = new CameraLoginForm(info.Ip, info.Brand, info.WebPort, info.OpenPorts)) dlg.ShowDialog(this);
        }

        private void OpenCameraWebPage(CameraDeviceInfo info)
        {
            try
            {
                int port = info.WebPort > 0 ? info.WebPort : 80;
                string scheme = (port == 443 || port == 8443) ? "https" : "http";
                string url = (port == 80 || port == 443) ? $"{scheme}://{info.Ip}" : $"{scheme}://{info.Ip}:{port}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开 Web 管理页面失败: " + ex.Message, "提示");
            }
        }

        private async Task<bool> IsTcpPortOpenAsync(string host, int port, int timeoutMs)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask && client.Connected)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>抓取 Web 管理页面的 Server 响应头 + 认证 Realm + 前一小段正文，拼一起用来做关键词匹配。</summary>
        private async Task<string> FetchHttpBannerAsync(string ip, int port)
        {
            string scheme = (port == 443 || port == 8443) ? "https" : "http";
            string url = (port == 80 || port == 443) ? $"{scheme}://{ip}/" : $"{scheme}://{ip}:{port}/";
            try
            {
                using (var resp = await cameraHttpClient.GetAsync(url))
                {
                    var sb = new StringBuilder();
                    if (resp.Headers.Server != null) sb.Append(resp.Headers.Server).Append(' ');
                    if (resp.Headers.WwwAuthenticate != null) sb.Append(string.Join(" ", resp.Headers.WwwAuthenticate)).Append(' ');
                    if (resp.Headers.Location != null) sb.Append(resp.Headers.Location.ToString()).Append(' ');
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(body)) sb.Append(body.Length > 1000 ? body.Substring(0, 1000) : body);
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        private string DetectCameraBrandFromBanner(string banner)
        {
            if (string.IsNullOrWhiteSpace(banner)) return null;
            string lower = banner.ToLowerInvariant();
            foreach (var (keyword, brand) in cameraBrandKeywords)
            {
                if (lower.Contains(keyword)) return brand;
            }
            return null;
        }

        /// <summary>ONVIF 探测：先问 GetDeviceInformation（部分杂牌免认证直接返回厂商/型号），失败再用 GetSystemDateAndTime 判断是否为 ONVIF 设备。</summary>
        private async Task<(bool ok, string info)> ProbeOnvifAsync(string ip, int port)
        {
            string scheme = (port == 443 || port == 8443) ? "https" : "http";
            string url = $"{scheme}://{ip}:{port}/onvif/device_service";
            string Envelope(string body) => "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body>" + body + "</s:Body></s:Envelope>";
            try
            {
                using (var content = new StringContent(Envelope("<GetDeviceInformation xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>"), Encoding.UTF8, "application/soap+xml"))
                using (var resp = await cameraHttpClient.PostAsync(url, content))
                {
                    string xml = await resp.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(xml) && xml.IndexOf("Envelope", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string manu = Regex.Match(xml, @"<[^>]*Manufacturer>([^<]*)<", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                        string model = Regex.Match(xml, @"<[^>]*Model>([^<]*)<", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                        string info = (manu + " " + model).Trim();
                        return (true, info);
                    }
                }
            }
            catch { }
            return (false, "");
        }

        /// <summary>RTSP 探测：发 OPTIONS，只要回 "RTSP/1.0" 就说明是流媒体设备，并取 Server 头。</summary>
        private async Task<(bool ok, string server)> ProbeRtspAsync(string ip, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connect = client.ConnectAsync(ip, port);
                    if (await Task.WhenAny(connect, Task.Delay(800)) != connect || !client.Connected) return (false, "");
                    var stream = client.GetStream();
                    byte[] req = Encoding.ASCII.GetBytes($"OPTIONS rtsp://{ip}:{port}/ RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: NetworkTools\r\n\r\n");
                    await stream.WriteAsync(req, 0, req.Length);
                    byte[] buf = new byte[1024];
                    var read = stream.ReadAsync(buf, 0, buf.Length);
                    if (await Task.WhenAny(read, Task.Delay(1200)) != read) return (false, "");
                    string text = Encoding.ASCII.GetString(buf, 0, Math.Max(0, read.Result));
                    if (!text.StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase)) return (false, "");
                    string server = Regex.Match(text, @"^Server:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    return (true, server);
                }
            }
            catch { }
            return (false, "");
        }

        private string TrimBannerForDisplay(string banner)
        {
            if (string.IsNullOrWhiteSpace(banner)) return "";
            string oneLine = Regex.Replace(banner, @"\s+", " ").Trim();
            return oneLine.Length > 140 ? oneLine.Substring(0, 140) + "..." : oneLine;
        }

        private async Task StartCameraScanAsync()
        {
            btnStartCameraScan.Enabled = false; btnStopCameraScan.Enabled = true;
            listViewCamera.Items.Clear();
            progressBarCamera.Value = 0;
            ctsCamera = new CancellationTokenSource();

            string localIp = GetActiveLocalIp();
            if (string.IsNullOrEmpty(localIp) || localIp.StartsWith("169.254") || localIp == "未连接网络")
            {
                MessageBox.Show("未检测到局域网有效 IP，请先接入网络！", "提示");
                btnStartCameraScan.Enabled = true; btnStopCameraScan.Enabled = false;
                return;
            }

            string subnet = localIp.Substring(0, localIp.LastIndexOf('.') + 1);
            lblCameraFooter.Text = $"正在扫描网段 {subnet}1 - 254 ，识别摄像头/监控设备...";

            int finished = 0; int found = 0;
            var tasks = new List<Task>();
            listViewCamera.BeginUpdate();

            // 【探测端口】
            // Web 管理端口：杂牌设备经常把 Web 放在 81/8081/8088/8888/8899/2020 等非标准端口；
            // RTSP 端口：554/8554，几乎所有摄像头/NVR 都开，是识别"杂牌"的最可靠线索；
            // 私有协议端口：8000=海康SDK，37777/37778=大华，3000=天地伟业SDK(弱线索)，6036=同为(TVT)，34567/34568=雄迈(Xiongmai)系OEM。
            int[] webPorts = { 80, 8080, 81, 8081, 8088, 8888, 8899, 2020, 5000, 443, 8443 };
            int[] rtspPorts = { 554, 8554 };
            int[] privPorts = { 8000, 37777, 37778, 3000, 6036, 34567, 34568 };
            int[] probePorts = webPorts.Concat(rtspPorts).Concat(privPorts).ToArray();

            using (var sem = new SemaphoreSlim(50))
            {
                for (int i = 1; i <= 254; i++)
                {
                    if (ctsCamera.Token.IsCancellationRequested) break;
                    string targetIp = subnet + i;
                    await sem.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (ctsCamera.Token.IsCancellationRequested) return;

                            var openFlags = await Task.WhenAll(probePorts.Select(p => IsTcpPortOpenAsync(targetIp, p, 500)));
                            var openPorts = new List<int>();
                            for (int k = 0; k < probePorts.Length; k++) if (openFlags[k]) openPorts.Add(probePorts[k]);
                            if (openPorts.Count == 0) return; // 这台主机相关端口全都没开，直接跳过，不是候选设备

                            var webOpen = webPorts.Where(p => openPorts.Contains(p)).ToList();
                            var rtspOpen = rtspPorts.Where(p => openPorts.Contains(p)).ToList();
                            int webPort = webOpen.Count > 0 ? webOpen[0] : -1;

                            string brand = null, basis = null;

                            // 1) Web 页面指纹：最多抓 3 个 Web 端口
                            var banners = new List<string>();
                            foreach (var wp in webOpen.Take(3))
                            {
                                string bn = await FetchHttpBannerAsync(targetIp, wp);
                                if (!string.IsNullOrEmpty(bn)) banners.Add(bn);
                            }
                            string banner = string.Join(" | ", banners);
                            brand = DetectCameraBrandFromBanner(banner);
                            if (brand != null) basis = "Web管理页面指纹匹配：" + TrimBannerForDisplay(banner);

                            // 2) ONVIF：很多杂牌不带品牌字样，但 ONVIF 会直接返回厂商/型号
                            bool onvifOk = false; string onvifInfo = "";
                            foreach (var wp in webOpen.Take(4))
                            {
                                var r = await ProbeOnvifAsync(targetIp, wp);
                                if (r.ok) { onvifOk = true; onvifInfo = r.info; if (!string.IsNullOrEmpty(onvifInfo)) break; }
                            }
                            if (brand == null && !string.IsNullOrEmpty(onvifInfo))
                            {
                                brand = DetectCameraBrandFromBanner(onvifInfo) ?? (onvifInfo + " (ONVIF)");
                                basis = "ONVIF设备信息：" + onvifInfo;
                            }

                            // 3) RTSP：读取 OPTIONS 响应里的 Server 头
                            bool rtspOk = false; string rtspBanner = "";
                            foreach (var rp in rtspOpen)
                            {
                                var r = await ProbeRtspAsync(targetIp, rp);
                                if (r.ok) { rtspOk = true; rtspBanner = r.server; break; }
                            }
                            if (brand == null && !string.IsNullOrEmpty(rtspBanner))
                            {
                                brand = DetectCameraBrandFromBanner(rtspBanner);
                                if (brand != null) basis = "RTSP服务特征：" + rtspBanner;
                            }

                            bool confirmed = brand != null;

                            // 4) 私有端口线索（仅供参考）
                            if (!confirmed)
                            {
                                bool has(int p) => openPorts.Contains(p);
                                if (has(8000)) { brand = "疑似海康威视 (Hikvision)"; basis = "端口特征：8000(SDK私有端口)开放，未匹配到指纹关键词，仅供参考"; }
                                else if (has(37777) || has(37778)) { brand = "疑似大华 (Dahua)"; basis = "端口特征：37777/37778(私有协议端口)开放，仅供参考"; }
                                else if (has(3000) && (rtspOk || onvifOk)) { brand = "疑似天地伟业 (Tiandy)"; basis = "端口特征：3000(SDK) + RTSP/ONVIF 同时开放，仅供参考"; }
                                else if (has(6036)) { brand = "疑似同为 (TVT)"; basis = "端口特征：6036(私有协议端口)开放，仅供参考"; }
                                else if (has(34567) || has(34568)) { brand = "疑似雄迈(Xiongmai)系 / 杂牌OEM设备"; basis = "端口特征：34567/34568(DVRIP私有协议端口)开放，仅供参考"; }
                                else if (onvifOk) { brand = "疑似网络摄像头 (ONVIF)"; basis = "ONVIF 服务有响应，但未识别出品牌"; }
                                else if (rtspOk) { brand = "疑似网络摄像头 (RTSP)"; basis = "RTSP 服务有响应(" + (string.IsNullOrEmpty(rtspBanner) ? "无Server头" : rtspBanner) + ")，但未识别出品牌"; }
                                else return; // 没有任何摄像头线索，跳过（避免把打印机/NAS/路由器管理页也列出来）
                            }
                            else if (basis == null) basis = "指纹匹配";

                            string mac = GetMacViaArp(targetIp);

                            var info = new CameraDeviceInfo
                            {
                                Ip = targetIp,
                                Brand = brand,
                                Mac = mac,
                                WebPort = webPort > 0 ? webPort : 80,
                                OpenPorts = string.Join(",", openPorts),
                                Basis = basis
                            };

                            Interlocked.Increment(ref found);
                            this.Invoke(new Action(() =>
                            {
                                var item = new ListViewItem(info.Brand);
                                item.SubItems.AddRange(new[] { info.Ip, info.OpenPorts, info.Mac, info.Basis });
                                item.Tag = info;
                                item.ForeColor = confirmed ? colorAccentG : colorWarn;
                                listViewCamera.Items.Add(item);
                            }));
                        }
                        catch { }
                        finally
                        {
                            sem.Release();
                            Interlocked.Increment(ref finished);
                            this.Invoke(new Action(() =>
                            {
                                progressBarCamera.Value = Math.Min(100, (int)(finished / 254.0 * 100));
                                lblCameraFooter.Text = $"进度: {finished}/254 | 已发现疑似摄像头/监控设备: {found} 台";
                            }));
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            listViewCamera.EndUpdate();
            btnStartCameraScan.Enabled = true; btnStopCameraScan.Enabled = false;
            if (!ctsCamera.Token.IsCancellationRequested)
                MessageBox.Show($"摄像头/监控设备扫描完成！共发现 {found} 台相关设备。双击列表中的一行即可登录设备预览/修改设置。", "扫描完成");
            else
                lblCameraFooter.Text = $"已手动停止 | 已发现疑似摄像头/监控设备: {found} 台";
        }
        #endregion
    }
}
