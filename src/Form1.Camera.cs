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
            ("hikvision", "海康威视 (Hikvision)"),
            ("海康", "海康威视 (Hikvision)"),
            ("dahua", "大华 (Dahua)"),
            ("大华", "大华 (Dahua)"),
            ("dvrdvs-webs", "大华 (Dahua)"),
            ("tp-link", "TP-LINK"),
            ("tplink", "TP-LINK"),
            ("melogin", "水星 (Mercury)"),
            ("mercurycom", "水星 (Mercury)"),
            ("水星", "水星 (Mercury)"),
            ("tvt", "天地伟业 (TVT)"),
            ("天地伟业", "天地伟业 (TVT)"),
            ("onvif", "ONVIF 通用网络摄像头"),
        };

        private void BuildTabCameraLayout()
        {
            GroupBox groupCtrl = new GroupBox { Text = " 📷 局域网摄像头 / 监控设备扫描 ", Location = new Point(15, 15), Size = new Size(880, 100), Font = fontBold, ForeColor = colorPrimary };
            tabCamera.Controls.Add(groupCtrl);
            AutoStretch(tabCamera, groupCtrl);

            Label lblDesc = new Label
            {
                Text = "自动扫描当前网段，通过 Web 管理页面指纹 + 常见私有协议端口特征，识别海康、大华、天地伟业、TP-LINK、水星等品牌的摄像头/NVR/路由器设备。扫描完成后，双击列表中的一行即可直接用浏览器打开该设备的 Web 管理页面。",
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

            // 双击直接打开该设备的 Web 管理页面
            listViewCamera.MouseDoubleClick += (s, e) =>
            {
                if (listViewCamera.SelectedItems.Count == 0) return;
                if (listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) OpenCameraWebPage(info);
            };

            // 右键菜单，跟其它页面（软件卸载/共享管理）的操作习惯保持一致
            var ctxMenu = new ContextMenuStrip();
            var ctxOpenWeb = new ToolStripMenuItem("在浏览器中打开 Web 管理页面");
            var ctxCopyIp = new ToolStripMenuItem("复制 IP 地址");
            ctxMenu.Items.AddRange(new ToolStripItem[] { ctxOpenWeb, ctxCopyIp });
            listViewCamera.ContextMenuStrip = ctxMenu;
            listViewCamera.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var hit = listViewCamera.HitTest(e.Location);
                if (hit.Item != null) hit.Item.Selected = true;
            };
            ctxOpenWeb.Click += (s, e) => { if (listViewCamera.SelectedItems.Count > 0 && listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) OpenCameraWebPage(info); };
            ctxCopyIp.Click += (s, e) => { if (listViewCamera.SelectedItems.Count > 0 && listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) { try { Clipboard.SetText(info.Ip); } catch { } } };

            btnStartCameraScan.Click += async (s, e) => { await StartCameraScanAsync(); };
            btnStopCameraScan.Click += (s, e) => { ctsCamera?.Cancel(); };
        }

        private void OpenCameraWebPage(CameraDeviceInfo info)
        {
            try
            {
                int port = info.WebPort > 0 ? info.WebPort : 80;
                string scheme = port == 443 ? "https" : "http";
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
            string scheme = port == 443 ? "https" : "http";
            string url = (port == 80 || port == 443) ? $"{scheme}://{ip}/" : $"{scheme}://{ip}:{port}/";
            try
            {
                using (var resp = await cameraHttpClient.GetAsync(url))
                {
                    var sb = new StringBuilder();
                    if (resp.Headers.Server != null) sb.Append(resp.Headers.Server).Append(' ');
                    if (resp.Headers.WwwAuthenticate != null) sb.Append(string.Join(" ", resp.Headers.WwwAuthenticate)).Append(' ');
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

            // 【常见品牌端口特征】80/8080/443 是几乎所有品牌都会开放的 Web 管理页面端口；
            // 8000 是海康 SDK 私有端口、37777 是大华私有协议端口、34567 是天地伟业/众多贴牌
            // (Xiongmai系OEM机型)常用的 DVRIP 私有协议端口——这三个不是 HTTP，不拿来抓网页，
            // 只当作品牌"命中线索"，配合 Web 指纹关键词一起判断。
            int[] probePorts = { 80, 8080, 443, 8000, 37777, 34567 };

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

                            var openFlags = await Task.WhenAll(probePorts.Select(p => IsTcpPortOpenAsync(targetIp, p, 400)));
                            var openPorts = new List<int>();
                            for (int k = 0; k < probePorts.Length; k++) if (openFlags[k]) openPorts.Add(probePorts[k]);
                            if (openPorts.Count == 0) return; // 这台主机相关端口全都没开，直接跳过，不是候选设备

                            int webPort = openPorts.Contains(80) ? 80 : (openPorts.Contains(8080) ? 8080 : (openPorts.Contains(443) ? 443 : -1));

                            string banner = webPort > 0 ? await FetchHttpBannerAsync(targetIp, webPort) : "";
                            string brand = DetectCameraBrandFromBanner(banner);
                            bool confirmed = brand != null;
                            string basis;

                            if (!confirmed)
                            {
                                if (openPorts.Contains(8000)) { brand = "疑似海康威视 (Hikvision)"; basis = "端口特征：8000(SDK私有端口)开放，未匹配到Web指纹关键词，仅供参考"; }
                                else if (openPorts.Contains(37777)) { brand = "疑似大华 (Dahua)"; basis = "端口特征：37777(私有协议端口)开放，未匹配到Web指纹关键词，仅供参考"; }
                                else if (openPorts.Contains(34567)) { brand = "疑似天地伟业 / OEM设备"; basis = "端口特征：34567(DVRIP私有协议端口)开放，未匹配到Web指纹关键词，仅供参考"; }
                                else return; // 只开了普通web端口、没有任何品牌线索的，不是本次要找的目标，跳过（避免把打印机/NAS/路由器管理页也当成摄像头列出来）
                            }
                            else
                            {
                                basis = "Web管理页面指纹匹配：" + TrimBannerForDisplay(banner);
                            }

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
                MessageBox.Show($"摄像头/监控设备扫描完成！共发现 {found} 台相关设备。双击列表中的一行即可用浏览器打开其 Web 管理页面。", "扫描完成");
            else
                lblCameraFooter.Text = $"已手动停止 | 已发现疑似摄像头/监控设备: {found} 台";
        }
        #endregion
    }
}
