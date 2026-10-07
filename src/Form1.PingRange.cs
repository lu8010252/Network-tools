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
        #region ====== 6.2 高级 Ping:网段 / TCP Ping ======
        /// <summary>根据网卡真实 Name 取它当前所在的 /24 网段，取不到（没连网/找不到该网卡）就返回 null。</summary>
        private string GetSubnetForIface(string ifaceName)
        {
            if (string.IsNullOrEmpty(ifaceName)) return null;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name != ifaceName) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var b = addr.Address.GetAddressBytes();
                        return $"{b[0]}.{b[1]}.{b[2]}.0/24";
                    }
                }
            }
            return null;
        }

        private void BuildRangePingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " 🌐 网段Ping参数 ", Location = new Point(15, 10), Size = new Size(850, 130), ForeColor = colorCyan, Font = fontBold };
            pageRangePing.Controls.Add(groupConfig);
            AutoStretch(pageRangePing, groupConfig);

            Label lblAd = new Label { Text = "🔌 选择网卡:", Location = new Point(15, 26), Size = new Size(80, 20), Font = fontNormal, ForeColor = textDark };
            comboRangeAdapter = new ComboBox { Location = new Point(95, 22), Size = new Size(280, 25), Font = fontNormal, DropDownStyle = ComboBoxStyle.DropDownList };

            // 【修复】原来这个下拉框只有"自动抓取主要适配器"一个选项，是摆设——
            // 既没有真正枚举本机网卡，也从没被下面的扫描逻辑读取过。现在真正枚举所有
            // 已连接、有IPv4地址的网卡，可以手动选具体某一张；选完网段会自动联动更新。
            var rangeIfaceMap = new Dictionary<string, string>(); // 下拉框显示文本 -> 网卡真实 Name
            comboRangeAdapter.Items.Add("自动检测(当前活动网卡)");
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                bool hasIPv4 = ni.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (!hasIPv4) continue;
                string display = $"{ni.Name} ({ni.Description})";
                rangeIfaceMap[display] = ni.Name;
                comboRangeAdapter.Items.Add(display);
            }
            comboRangeAdapter.SelectedIndex = 0;
            groupConfig.Controls.AddRange(new Control[] { lblAd, comboRangeAdapter });

            Label lblSn = new Label { Text = "🌐 网段:", Location = new Point(15, 60), Size = new Size(60, 20), Font = fontNormal, ForeColor = textDark };
            comboRangeSubnet = new ComboBox { Location = new Point(95, 56), Size = new Size(150, 25), Font = fontNormal };
            comboRangeSubnet.Items.AddRange(new string[] { "192.168.0.0/24", "192.168.1.0/24", "10.0.0.0/24" });

            // 【修复】默认网段不再写死成 192.168.1.0/24，改成读取当前真实活动网卡所在的网段，
            // 你现在是 192.168.0.x 就默认显示 192.168.0.0/24，不用每次手动切换。
            string autoSubnet = GetSubnetForIface(currentIfaceName) ?? "192.168.1.0/24";
            if (!comboRangeSubnet.Items.Contains(autoSubnet)) comboRangeSubnet.Items.Insert(0, autoSubnet);
            comboRangeSubnet.Text = autoSubnet;

            comboRangeAdapter.SelectedIndexChanged += (s, e) =>
            {
                string subnet = null;
                if (comboRangeAdapter.SelectedIndex == 0)
                {
                    subnet = GetSubnetForIface(currentIfaceName);
                }
                else if (rangeIfaceMap.TryGetValue(comboRangeAdapter.SelectedItem.ToString(), out string ifaceName))
                {
                    subnet = GetSubnetForIface(ifaceName);
                }
                if (subnet != null)
                {
                    if (!comboRangeSubnet.Items.Contains(subnet)) comboRangeSubnet.Items.Insert(0, subnet);
                    comboRangeSubnet.Text = subnet;
                }
            };

            Label lblHint = new Label { Text = "(动态网段自适应修复版，支持任意 standard C 类掩码拓扑)", Location = new Point(255, 60), Size = new Size(400, 20), Font = fontNormal, ForeColor = Color.Gray };
            groupConfig.Controls.AddRange(new Control[] { lblSn, comboRangeSubnet, lblHint });

            Label lblT = new Label { Text = "⏱️ 超时:", Location = new Point(15, 95), Size = new Size(55, 20), Font = fontNormal, ForeColor = textDark };
            txtRangeTimeout = new TextBox { Text = "5", Location = new Point(70, 91), Size = new Size(35, 23), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblSec = new Label { Text = "秒", Location = new Point(110, 95), Size = new Size(20, 20), Font = fontNormal, ForeColor = Color.Gray };
            Label lblSz = new Label { Text = "📦 包大小:", Location = new Point(140, 95), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            txtRangeSize = new TextBox { Text = "32", Location = new Point(210, 91), Size = new Size(40, 23), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblBy = new Label { Text = "字节", Location = new Point(255, 95), Size = new Size(35, 20), Font = fontNormal, ForeColor = Color.Gray };
            Label lblTh = new Label { Text = "⚡ 线程:", Location = new Point(300, 95), Size = new Size(55, 20), Font = fontNormal, ForeColor = textDark };
            txtRangeThreads = new TextBox { Text = "50", Location = new Point(355, 91), Size = new Size(45, 23), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            btnStartRange = new Button { Text = "🚀 开始扫描", Location = new Point(420, 88), Size = new Size(95, 30), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite };
            btnStopRange = new Button { Text = "🔲 停止", Location = new Point(525, 88), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopRange.FlatAppearance.BorderColor = colorBorder;
            btnExportRange = new Button { Text = "💾 导出", Location = new Point(605, 88), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnExportRange.FlatAppearance.BorderColor = colorBorder;

            groupConfig.Controls.AddRange(new Control[] { lblT, txtRangeTimeout, lblSec, lblSz, txtRangeSize, lblBy, lblTh, txtRangeThreads, btnStartRange, btnStopRange, btnExportRange });

            Panel panelLegend = new Panel { Location = new Point(700, 90), Size = new Size(140, 30) };
            string[] legendNames = { "在线", "离线" };
            Color[] legendColors = { colorAccentG, Color.DarkGray };
            for (int i = 0; i < 2; i++)
            {
                Label block = new Label { Location = new Point(i * 70, 6), Size = new Size(12, 12), BackColor = legendColors[i] };
                Label txt = new Label { Text = legendNames[i], Location = new Point(i * 70 + 16, 4), Size = new Size(40, 18), Font = fontNormal };
                panelLegend.Controls.AddRange(new Control[] { block, txt });
            }
            groupConfig.Controls.Add(panelLegend);
            AutoStretch(groupConfig, panelLegend);

            lblRangeFooter = new Label { Text = "就绪 | 总计: 254 | 已扫: 0 | 在线: 0 | 离线: 0 | 在线率: 0%", Location = new Point(15, 145), Size = new Size(850, 24), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageRangePing.Controls.Add(lblRangeFooter);
            AutoStretch(pageRangePing, lblRangeFooter);

            panelRangeGrid = new FlowLayoutPanel
            {
                Location = new Point(15, 175),
                Size = new Size(850, 400),
                AutoScroll = true,
                WrapContents = true,
                BorderStyle = BorderStyle.FixedSingle,
                // 【彻底修复】真正的根因找到了：FlowLayoutPanel 先按"没有滚动条"时的可用宽度
                // 计算每行能放几列，算完之后才发现内容纵向超出、需要出现竖直滚动条——
                // 但滚动条一出现就会占掉右侧 17~20px 空间，这时最后一列"本来刚好放得下"，
                // 滚动条一占位就被顶到条子底下裁掉一截。加一个跟滚动条同宽的右侧 Padding，
                // 让它在"决定每行放几列"这一步就提前把这块空间让出来，不会再出现这个问题。
                // 【再修复】底部同理：FlowLayoutPanel 计算 AutoScroll 可滚动总高度时经常会
                // 短算最后一整行，导致明明有255个方块，拖到底之前就"到底"了，最后一行(如241-255)
                // 永远看不到。这里额外加一块底部 Padding，强制滚动范围多留出至少一行的高度。
                Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth + 6, 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            pageRangePing.Controls.Add(panelRangeGrid);

            panelRangeGrid.SuspendLayout();
            Label lastLabel = null;
            // 【优化】不再包含 .255（广播地址），广播地址永远"探测得通"，扫描起来没有意义还会造成误导。
            for (int i = 1; i <= 254; i++)
            {
                // 【修复】原来 Size 写死 46px，装不下 "[ 100 ]" 这种三位数的内容会被裁切。
                // 改成 AutoSize + MinimumSize，方块宽度跟着实际文字自动撑开，不管几位数都能完整显示。
                Label lblBox = new Label
                {
                    Text = $"{i}",
                    AutoSize = true,
                    MinimumSize = new Size(44, 22),
                    Padding = new Padding(4, 3, 4, 3),
                    Margin = new Padding(3),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(240, 242, 245),
                    ForeColor = textDark,
                    Font = new Font("Consolas", 8.5F, FontStyle.Bold),
                    BorderStyle = BorderStyle.FixedSingle
                };
                rangeGridLabels[i] = lblBox;
                panelRangeGrid.Controls.Add(lblBox);
                lastLabel = lblBox;
            }

            // 【真正修复】上一版加的占位控件只有 1px 宽，在自动换行容器里会直接被塞进
            // 最后一行的行尾（跟第255号方块挤在同一行），根本没有另起一行、没撑出任何高度，
            // 等于白加。这里用 SetFlowBreak 强制在第255号方块后面换行，占位控件才会真正
            // 独占新的一整行，撑出实打实的额外可滚动高度，确保能拖到底看到 255。
            if (lastLabel != null) panelRangeGrid.SetFlowBreak(lastLabel, true);
            panelRangeGrid.Controls.Add(new Label { Text = "", AutoSize = false, Size = new Size(1, 60), Margin = new Padding(0) });
            panelRangeGrid.ResumeLayout();


            btnStartRange.Click += async (s, e) => { await RunRangePingAsync(); };
            btnStopRange.Click += (s, e) => ctsRange?.Cancel();

            // 【修复】这个按钮之前只是摆在界面上，从来没绑定过点击事件，点了毫无反应。
            btnExportRange.Click += (s, e) =>
            {
                if (rangePingResults.Count == 0) { MessageBox.Show("还没有扫描结果，先点\"开始扫描\"再导出。", "提示"); return; }
                try
                {
                    using (var sfd = new SaveFileDialog { Title = "导出网段Ping结果", Filter = "CSV文件|*.csv|文本文件|*.txt", FileName = $"网段Ping结果_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
                    {
                        if (sfd.ShowDialog() != DialogResult.OK) return;
                        var sb = new StringBuilder();
                        sb.AppendLine("IP地址,状态");
                        lock (rangePingResults)
                        {
                            foreach (var (ip, online) in rangePingResults)
                                sb.AppendLine($"{ip},{(online ? "在线" : "离线")}");
                        }
                        File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                        MessageBox.Show("导出成功。", "完成");
                    }
                }
                catch (Exception ex)
                {
                    // 【新增】之前导出失败是静默的，看不出原因。加个提示，方便定位到底是权限问题
                    // 还是路径问题还是别的什么。
                    MessageBox.Show("导出失败: " + ex.Message, "出错了");
                }
            };
        }

        private async Task RunRangePingAsync()
        {
            btnStartRange.Enabled = false; btnStopRange.Enabled = true;
            ctsRange = new CancellationTokenSource();

            for (int i = 1; i <= 254; i++)
            {
                rangeGridLabels[i].BackColor = Color.FromArgb(240, 242, 245);
                rangeGridLabels[i].ForeColor = textDark;
            }

            string rawText = comboRangeSubnet.Text.Split('/')[0].Trim();
            string ipBase = rawText.Substring(0, rawText.LastIndexOf('.') + 1);
            rangePingResults.Clear(); // 【修复】每次开始扫描前清空上一轮的记录，供"导出"按钮使用

            // 【优化】之前这里的Ping超时是写死的800ms，"超时"那个输入框根本没被读取过，是摆设。
            // 这次真正读取用户填的值（单位是秒，输入框里默认"5"），换算成毫秒。
            if (!double.TryParse(txtRangeTimeout.Text.Trim(), out double timeoutSec) || timeoutSec <= 0) timeoutSec = 5;
            int timeoutMs = Math.Max(200, (int)(timeoutSec * 1000));

            int scanned = 0, online = 0, offline = 0;
            var tasks = new List<Task>();
            using (var sem = new SemaphoreSlim(30))
            {
                // 【优化】不再扫 .255（广播地址）：每次都会显示"通"，是假阳性，没有实际参考价值。
                for (int i = 1; i <= 254; i++)
                {
                    if (ctsRange.Token.IsCancellationRequested) break;
                    int currentId = i;
                    string targetIp = ipBase + currentId;
                    await sem.WaitAsync();

                    tasks.Add(Task.Run(async () => {
                        bool success = false;
                        try
                        {
                            // 【优化】先用ARP硬件层探测（跟"局域网主机发现"一致的做法），能抓到
                            // 那些屏蔽了ICMP、但网络层其实在线的设备（不少打印机/IoT/安全加固过
                            // 的设备都会屏蔽ping）。ARP探测不到再退回Ping，Ping超时了再补一次重试，
                            // 避免偶尔丢一个包就被误判成"离线"。
                            if (GetMacViaArp(targetIp) != "—")
                            {
                                success = true;
                            }
                            else
                            {
                                using (Ping p = new Ping())
                                {
                                    PingReply r = await p.SendPingAsync(targetIp, timeoutMs);
                                    success = (r.Status == IPStatus.Success);
                                    if (!success)
                                    {
                                        PingReply r2 = await p.SendPingAsync(targetIp, timeoutMs);
                                        success = (r2.Status == IPStatus.Success);
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            sem.Release();
                            scanned++;
                            lock (rangePingResults) { rangePingResults.Add((targetIp, success)); }
                            this.Invoke(new Action(() => {
                                if (success)
                                {
                                    online++;
                                    rangeGridLabels[currentId].BackColor = colorAccentG;
                                    rangeGridLabels[currentId].ForeColor = colorWhite;
                                }
                                else
                                {
                                    offline++;
                                    rangeGridLabels[currentId].BackColor = Color.DarkGray;
                                    rangeGridLabels[currentId].ForeColor = colorWhite;
                                }
                                lblRangeFooter.Text = $"扫描中 | 总计: 254 | 已扫: {scanned} | 在线: {online} | 离线: {offline} | 在线率: {(int)((double)online / 254 * 100)}%";
                            }));
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }
            btnStartRange.Enabled = true; btnStopRange.Enabled = false;
        }

        private void BuildTcpPingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " ⚙️ TCP Ping参数配置 ", Location = new Point(15, 15), Size = new Size(850, 150), ForeColor = colorCyan, Font = fontBold };
            pageTcpPing.Controls.Add(groupConfig);
            AutoStretch(pageTcpPing, groupConfig);

            Label lblTarget = new Label { Text = "目标主机:", Location = new Point(15, 32), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            comboTcpTarget = new ComboBox { Location = new Point(90, 28), Size = new Size(380, 25), Font = fontNormal };
            comboTcpTarget.Items.AddRange(new string[] { "www.baidu.com", "127.0.0.1" });
            comboTcpTarget.Text = "www.baidu.com";

            Label lblP = new Label { Text = "🔌 目标端口:", Location = new Point(490, 32), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtTcpPort = new TextBox { Text = "80", Location = new Point(580, 28), Size = new Size(80, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            Label lblC = new Label { Text = "🔢 测试次数:", Location = new Point(15, 75), Size = new Size(80, 20), Font = fontNormal, ForeColor = textDark };
            txtTcpCount = new TextBox { Text = "10", Location = new Point(95, 71), Size = new Size(80, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            Label lblT = new Label { Text = "⏱️ 超时时间:", Location = new Point(490, 75), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtTcpTimeout = new TextBox { Text = "3", Location = new Point(580, 71), Size = new Size(60, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblS = new Label { Text = "秒", Location = new Point(645, 75), Size = new Size(25, 20), Font = fontNormal, ForeColor = Color.Gray };

            groupConfig.Controls.AddRange(new Control[] { lblTarget, comboTcpTarget, lblP, txtTcpPort, lblC, txtTcpCount, lblT, txtTcpTimeout, lblS });

            string[] appLabels = { "HTTP:80", "HTTPS:443", "SSH:22", "RDP:3389", "MySQL:3306", "Redis:6379" };
            string[] appPorts = { "80", "443", "22", "3389", "3306", "6379" };
            for (int i = 0; i < appLabels.Length; i++)
            {
                Button btnPort = new Button { Text = appLabels[i], Location = new Point(15 + (i * 105), 110), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat, Font = fontNormal, BackColor = colorWhite, ForeColor = textDark };
                btnPort.FlatAppearance.BorderColor = colorBorder;
                string portVal = appPorts[i];
                btnPort.Click += (s, e) => txtTcpPort.Text = portVal;
                groupConfig.Controls.Add(btnPort);
            }

            btnStartTcp = new Button { Text = "🚀 开始TCP Ping", Location = new Point(660, 108), Size = new Size(150, 30), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite };
            btnStartTcp.FlatAppearance.BorderSize = 0;
            btnStopTcp = new Button { Text = "🔲 停止", Location = new Point(660, 68), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopTcp.FlatAppearance.BorderColor = colorBorder;

            groupConfig.Controls.AddRange(new Control[] { btnStartTcp, btnStopTcp });

            GroupBox groupResult = new BoxGroup { Text = " 📊 TCP Ping测试结果 ", Location = new Point(15, 175), Size = new Size(830, 250), ForeColor = colorCyan, Font = fontBold };
            pageTcpPing.Controls.Add(groupResult);

            Panel cyanBar = new Panel { Location = new Point(10, 22), Size = new Size(810, 6), BackColor = colorCyan };
            txtTcpConsole = new TextBox { Location = new Point(10, 28), Size = new Size(810, 210), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 10F), BackColor = colorWhite, ForeColor = textDark };
            groupResult.Controls.AddRange(new Control[] { cyanBar, txtTcpConsole });
            AutoStretch(groupResult, cyanBar);
            AutoStretch(groupResult, txtTcpConsole, width: true, height: true, rightMargin: 10, bottomMargin: 10);

            lblTcpFooter = new Label { Text = "就绪 | 已发送: 0 | 成功: 0 | 失败: 0 | 成功率: 0%", Location = new Point(15, 435), Size = new Size(830, 28), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageTcpPing.Controls.Add(lblTcpFooter);
            AutoStretch(pageTcpPing, lblTcpFooter);
            AutoStickBottom(pageTcpPing, lblTcpFooter);
            AutoStretch(pageTcpPing, groupResult, width: true, height: true, rightMargin: 15, bottomMargin: lblTcpFooter.Height + 20);

            btnStartTcp.Click += async (s, e) => { await RunTcpPingAsync(); };
            btnStopTcp.Click += (s, e) => ctsTcp?.Cancel();
        }

        private class BoxGroup : GroupBox { }

        private async Task RunTcpPingAsync()
        {
            string host = comboTcpTarget.Text.Trim();
            if (!int.TryParse(txtTcpPort.Text, out int port) || port <= 0 || port > 65535) return;
            if (!int.TryParse(txtTcpCount.Text, out int count) || count <= 0) return;
            if (!int.TryParse(txtTcpTimeout.Text, out int timeoutSec) || timeoutSec <= 0) return;

            btnStartTcp.Enabled = false; btnStopTcp.Enabled = true;
            txtTcpConsole.Clear();
            txtTcpConsole.AppendText($"正在探测 {host} 对准端口: {port} ...\r\n\r\n");
            ctsTcp = new CancellationTokenSource();

            int sent = 0, success = 0, fail = 0;
            for (int i = 0; i < count; i++)
            {
                if (ctsTcp.Token.IsCancellationRequested) break;
                sent++;
                Stopwatch sw = Stopwatch.StartNew();
                bool ok = false;
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        var connectTask = client.ConnectAsync(host, port);
                        var delayTask = Task.Delay(timeoutSec * 1000);
                        var completedTask = await Task.WhenAny(connectTask, delayTask);
                        if (completedTask == connectTask && client.Connected) ok = true;
                    }
                }
                catch { }
                sw.Stop();

                if (ok)
                {
                    success++;
                    txtTcpConsole.AppendText($"来自 {host} 的 TCP 握手回复: 端口={port} 响应时间={sw.ElapsedMilliseconds}ms\r\n");
                }
                else
                {
                    fail++;
                    txtTcpConsole.AppendText($"对准 {host}:{port} 的探测握手失败或超时连接。\r\n");
                }

                double rate = sent > 0 ? (double)success / sent * 100 : 0;
                lblTcpFooter.Text = $"测试中 | 已发送: {sent} | 成功: {success} | 失败: {fail} | 成功率: {(int)rate}%";
                try { await Task.Delay(1000, ctsTcp.Token); } catch { break; }
            }
            btnStartTcp.Enabled = true; btnStopTcp.Enabled = false;
        }
        #endregion
    }
}
