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
        #region ====== 5.1 局域网主机发现 (ARP 扫描) ======
        private void BuildTabScanLayout()
        {
            btnStartScan = CreateStyledButton("开始 ARP 交叉精准扫描", 15, 15, 200, 35);
            progressBarScan = new ProgressBar { Location = new Point(230, 21), Size = new Size(400, 23), Style = ProgressBarStyle.Continuous };
            // 【新增】导出功能：把当前扫描结果列表直接导出成 CSV，复用了代码里已有的
            // ExportListViewToCsv 通用方法（其它几个 Tab 的导出按钮也是用它）。
            btnExportScan = CreateStyledButton("导出结果", 645, 15, 95, 35, primary: false);
            btnExportScan.Click += (s, e) => {
                if (listViewScan.Items.Count == 0)
                {
                    MessageBox.Show("当前没有可导出的扫描结果，请先执行扫描。", "提示");
                    return;
                }
                ExportListViewToCsv(listViewScan, "局域网主机发现结果");
            };
            tabScan.Controls.AddRange(new Control[] { btnStartScan, progressBarScan, btnExportScan });

            // 【修复】原来这两个标题 Label 既没设 AutoSize，也没给 Size，
            // WinForms 新建 Label 默认给的是 100x23 的固定小方框，文字必然被框死截断
            // （"底层链路层强力审计算法..."显示成"底层链路层强力"就是这个原因，跟DPI/窗口大小无关）。
            // 加上 AutoSize=true，方框跟着文字实际宽度自动撑开，不会再截断。
            Panel pDash = new Panel { Location = new Point(15, 65), Size = new Size(420, 95), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(245, 247, 250) };
            lblTotalScan = new Label { Text = "检测范围: 等待扫描...", Location = new Point(15, 38), Size = new Size(300, 20) };
            lblActiveCount = new Label { Text = "当前在线设备: 0 台", Location = new Point(15, 63), Size = new Size(300, 20), ForeColor = colorAccentG, Font = fontBold };
            pDash.Controls.AddRange(new Control[] { new Label { Text = "资产高精扫描状态", Location = new Point(10, 8), Font = fontBold, AutoSize = true }, lblTotalScan, lblActiveCount });

            // 【真正的根因】上一版给 panelPolicy 去掉了 Anchor，圆角和文字是不截断了，
            // 但代价是不再跟着窗口变宽——这只是绕开了症状，没有解决病根。
            // 真正的病根是：这套手动 this.Scale() 做 DPI 缩放的逻辑，跟 WinForms 原生
            // Anchor=Right/Bottom（锚定右/下边自动拉伸）机制混用时，会导致锚定控件的
            // 宽/高被错误地越滚越大，一路撑到窗口实际可视区域之外——文字、圆角、
            // 甚至 listViewScan 的滚动条，只要用了 Anchor=Right，都可能被顶到看不见的地方，
            // 这也是"拉不到底"的真正原因。
            // 彻底修法：这个 Tab 里凡是要跟着窗口变化尺寸的控件，一律不用 Anchor，
            // 改成监听 tabScan 的 Resize 事件、用当前真实宽高现算，不再依赖那套
            // 会跟自定义 DPI 缩放打架的锚定机制——这样无论 Scale() 怎么折腾，
            // 尺寸都是按当前真实窗口现算出来的，不会再跑出可视区域。
            Panel panelPolicy = new Panel { Location = new Point(450, 65), Size = new Size(445, 95), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(242, 250, 244) };
            Label lblPolicyContext = new Label { Text = "底层物理层特种 ARP 广播主动审计 + 经典 ICMP 双核交叉扫描。无缝穿透并精准识别开启了防火墙深度隐身的设备。", Location = new Point(12, 35), Size = new Size(420, 55), ForeColor = Color.DimGray, Font = fontNormal };
            panelPolicy.Controls.AddRange(new Control[] { new Label { Text = "底层链路层强力审计算法 (高准度修复版)", Location = Point.Empty, Font = fontBold, ForeColor = colorAccentG, AutoSize = true }, lblPolicyContext });
            tabScan.Controls.AddRange(new Control[] { pDash, panelPolicy });
            Stylize(pDash, 14);
            Stylize(panelPolicy, 14);
            // panelPolicy 尺寸变化时（下面 tabScan.Resize 里手动赋值触发），让内部文字框跟着变宽，
            // 同时 Stylize() 内部已经订阅了 panelPolicy.Resize 来重算圆角区域，两者都会正确触发。
            panelPolicy.Resize += (s, e) => { lblPolicyContext.Width = Math.Max(100, panelPolicy.Width - 24); };

            listViewScan = new ListView { Location = new Point(15, 175), Size = new Size(880, 400), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Consolas", 9.5F) };
            listViewScan.Columns.AddRange(new ColumnHeader[] { new ColumnHeader { Text = "主机 IP 地址", Width = 200 }, new ColumnHeader { Text = "硬件 MAC 地址", Width = 200 }, new ColumnHeader { Text = "探测状态", Width = 240 }, new ColumnHeader { Text = "设备制造厂商 (OUI)", Width = 180 } });
            tabScan.Controls.Add(listViewScan);
            AutoFillLastColumn(listViewScan, 180);

            // 【核心修复】不再用 Anchor，改成 tabScan 尺寸变化时手动现算每个"要跟着变化"的
            // 控件的宽/高，直接用 tabScan.ClientSize 这个当下真实值，不经过 WinForms 的
            // Anchor 缓存机制，从根上避免跟 this.Scale() 打架、把控件撑出可视区域的问题。
            void RelayoutScanTab()
            {
                int w = tabScan.ClientSize.Width;
                int h = tabScan.ClientSize.Height;
                if (w <= 0 || h <= 0) return;

                btnExportScan.Left = Math.Max(btnStartScan.Right + 15, w - 15 - btnExportScan.Width);
                progressBarScan.Width = Math.Max(100, btnExportScan.Left - 15 - progressBarScan.Left);

                panelPolicy.Width = Math.Max(200, w - panelPolicy.Left - 15);

                listViewScan.Width = Math.Max(200, w - listViewScan.Left - 15);
                listViewScan.Height = Math.Max(100, h - listViewScan.Top - 15);
            }
            tabScan.Resize += (s, e) => RelayoutScanTab();
            // 构建时先手动跑一次兜底：此时 tabScan 可能还没被真正 Dock/铺满，尺寸不一定准，
            // 但没关系，后面 Dock=Fill 生效、DPI 缩放跑完，都会再次触发 Resize 重新算一遍。
            RelayoutScanTab();

            btnStartScan.Click += async (s, e) => { await StartNetworkScanAsync(); };
        }

        private async Task StartNetworkScanAsync()
        {
            btnStartScan.Enabled = false;
            listViewScan.Items.Clear();
            progressBarScan.Value = 0;

            string localIp = GetActiveLocalIp();
            if (string.IsNullOrEmpty(localIp) || localIp.StartsWith("169.254") || localIp == "未连接网络")
            {
                MessageBox.Show("未检测到局域网有效 IP，请先接入网络！", "提示");
                btnStartScan.Enabled = true;
                return;
            }

            string subnet = localIp.Substring(0, localIp.LastIndexOf('.') + 1);
            lblTotalScan.Text = $"检测范围: {subnet}1 - 254";

            int finishedCount = 0;
            var tasks = new List<Task>();

            // 【修复：滚动条滚不到底】之前的写法是 60 个线程并发探测，每测完一台在线设备
            // 就单独跨线程 this.Invoke 一次、直接 Add 进 listViewScan，外面又包了一层
            // BeginUpdate/EndUpdate（长时间关闭重绘）。这种"长时间关闭重绘 + 高频跨线程插入"
            // 的组合，是 WinForms 原生 ListView 控件的一个已知坑：数据其实都进了 Items 集合
            // （所以"当前在线设备"计数是对的），但控件内部的滚动条量程（可滚动总高度）会跟丢，
            // 表现出来就是拖到某个位置就死活拖不动，下面还有几十台设备却看不到、滚不到。
            // 
            // 彻底的解法：扫描期间只把结果收集到线程安全的字典里（不碰 UI 控件），
            // 全部 254 个地址都测完之后，只对 listViewScan 做一次性的 Clear+AddRange，
            // 从根上避免"高频跨线程插入导致滚动条失步"这个场景，不会再复现。
            var resultsBySeq = new System.Collections.Concurrent.ConcurrentDictionary<int, ListViewItem>();

            using (var semaphore = new SemaphoreSlim(60))
            {
                for (int i = 1; i <= 254; i++)
                {
                    string targetIp = subnet + i;
                    int seq = i;
                    await semaphore.WaitAsync();

                    tasks.Add(Task.Run(async () => {
                        bool isOnline = false;
                        string macAddress = "—";
                        string statusText = "离线";

                        try
                        {
                            if (IPAddress.TryParse(targetIp, out IPAddress ip))
                            {
                                uint destIp = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
                                byte[] macBuf = new byte[6];
                                uint macLen = (uint)macBuf.Length;

                                if (SendARP(destIp, 0, macBuf, ref macLen) == 0)
                                {
                                    isOnline = true;
                                    macAddress = BitConverter.ToString(macBuf);
                                    statusText = "在线 (ARP硬件链路响应)";
                                }
                                else
                                {
                                    using (Ping p = new Ping())
                                    {
                                        PingReply reply = await p.SendPingAsync(targetIp, 300);
                                        if (reply.Status != IPStatus.Success)
                                        {
                                            // 【优化】300ms超时单次探测很容易被网络抖动误伤，
                                            // 补一次重试再下"离线"结论，减少误判。
                                            reply = await p.SendPingAsync(targetIp, 300);
                                        }
                                        if (reply.Status == IPStatus.Success)
                                        {
                                            isOnline = true;
                                            statusText = $"在线 (ICMP响应延迟: {reply.RoundtripTime}ms)";
                                            macAddress = GetMacViaArp(targetIp);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            semaphore.Release();
                            if (isOnline)
                            {
                                string vendor = GetVendorName(macAddress);
                                var item = new ListViewItem(targetIp);
                                item.SubItems.AddRange(new string[] { macAddress, statusText, vendor });
                                item.ForeColor = statusText.Contains("ARP") ? colorAccentG : Color.DarkOrange;
                                resultsBySeq[seq] = item;
                            }

                            this.Invoke(new Action(() => {
                                finishedCount++;
                                progressBarScan.Value = Math.Min(100, (int)((finishedCount / 254.0) * 100));
                                lblActiveCount.Text = $"当前在线设备: {resultsBySeq.Count} 台";
                            }));
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            // 按 IP 地址数值大小（也就是探测序号）从小到大整齐排列，方便看。
            var sortedItems = resultsBySeq
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value)
                .ToArray();

            listViewScan.BeginUpdate();
            listViewScan.Items.Clear();
            listViewScan.Items.AddRange(sortedItems);
            listViewScan.EndUpdate();

            MessageBox.Show($"双核高精交叉审计完成！精准截获当前在线活跃资产共 {listViewScan.Items.Count} 台。", "扫描完成");
            btnStartScan.Enabled = true;
        }
        #endregion
    }
}
