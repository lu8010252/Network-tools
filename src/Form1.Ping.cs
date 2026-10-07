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
        #region ====== 6.1 高级 Ping:单个 / 持续 / 批量 ======
        private void BuildTabPingLayout()
        {
            subTabPing = new TabControl
            {
                Font = fontBold,
                Dock = DockStyle.Fill
            };
            tabPing.Controls.Add(subTabPing);

            pageSinglePing = new TabPage(" 📍 单个Ping ") { BackColor = colorWhite };
            pageKeepPing = new TabPage(" 🔄 持续Ping ") { BackColor = colorWhite };
            pageBatchPing = new TabPage(" 📦 批量Ping ") { BackColor = colorWhite };
            pageRangePing = new TabPage(" 🌐 网段Ping ") { BackColor = colorWhite };
            pageTcpPing = new TabPage(" ⚡ TCP Ping ") { BackColor = colorWhite };

            subTabPing.TabPages.AddRange(new TabPage[] { pageSinglePing, pageKeepPing, pageBatchPing, pageRangePing, pageTcpPing });

            BuildSinglePingModule();
            BuildKeepPingModule();
            BuildBatchPingModule();
            BuildRangePingModule();
            BuildTcpPingModule();
        }

        private void BuildSinglePingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " ⚙️ Ping参数配置 ", Location = new Point(15, 15), Size = new Size(850, 120), ForeColor = colorCyan, Font = fontBold };
            pageSinglePing.Controls.Add(groupConfig);
            AutoStretch(pageSinglePing, groupConfig);

            Label lblTarget = new Label { Text = "目标主机:", Location = new Point(15, 32), Size = new Size(70, 20), ForeColor = textDark, Font = fontNormal };
            comboSingleTarget = new ComboBox { Location = new Point(90, 28), Size = new Size(250, 25), Font = fontNormal };
            comboSingleTarget.Items.AddRange(new string[] { "223.5.5.5", "114.114.114.114", "8.8.8.8", "www.baidu.com" });
            comboSingleTarget.Text = "www.baidu.com";

            Label lblCount = new Label { Text = "测试次数:", Location = new Point(370, 32), Size = new Size(70, 20), ForeColor = textDark, Font = fontNormal };
            txtSingleCount = new TextBox { Text = "4", Location = new Point(445, 28), Size = new Size(80, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            Label lblSize = new Label { Text = "数据包大小:", Location = new Point(15, 75), Size = new Size(80, 20), ForeColor = textDark, Font = fontNormal };
            txtSingleSize = new TextBox { Text = "64", Location = new Point(95, 71), Size = new Size(70, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblUnit = new Label { Text = "字节", Location = new Point(170, 75), Size = new Size(35, 20), ForeColor = Color.Gray, Font = fontNormal };

            groupConfig.Controls.AddRange(new Control[] { lblTarget, comboSingleTarget, lblCount, txtSingleCount, lblSize, txtSingleSize, lblUnit });

            string[] sizeLabels = { "32B", "1KB", "4KB", "8KB" };
            int[] sizeValues = { 32, 1024, 4096, 8192 };
            for (int i = 0; i < sizeLabels.Length; i++)
            {
                Button btnSize = new Button { Text = sizeLabels[i], Location = new Point(220 + (i * 65), 70), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, Font = fontNormal, BackColor = colorWhite, ForeColor = textDark };
                btnSize.FlatAppearance.BorderColor = colorBorder;
                int val = sizeValues[i];
                btnSize.Click += (s, e) => txtSingleSize.Text = val.ToString();
                groupConfig.Controls.Add(btnSize);
            }

            btnStartSingle = new Button { Text = "🚀 开始Ping", Location = new Point(500, 68), Size = new Size(110, 32), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite, Font = fontBold };
            btnStartSingle.FlatAppearance.BorderSize = 0;
            btnStopSingle = new Button { Text = "🔲 停止", Location = new Point(620, 68), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopSingle.FlatAppearance.BorderColor = colorBorder;
            groupConfig.Controls.AddRange(new Control[] { btnStartSingle, btnStopSingle });

            GroupBox groupResult = new GroupBox { Text = " 📊 Ping测试结果 ", Location = new Point(15, 145), Size = new Size(850, 330), ForeColor = colorCyan, Font = fontBold };
            pageSinglePing.Controls.Add(groupResult);

            Panel cyanBar = new Panel { Location = new Point(10, 22), Size = new Size(830, 6), BackColor = colorCyan };
            txtSingleConsole = new TextBox { Location = new Point(10, 28), Size = new Size(830, 290), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 10F), BackColor = colorWhite, ForeColor = textDark };
            groupResult.Controls.AddRange(new Control[] { cyanBar, txtSingleConsole });
            AutoStretch(groupResult, cyanBar);
            AutoStretch(groupResult, txtSingleConsole, width: true, height: true, rightMargin: 10, bottomMargin: 10);

            lblSingleFooter = new Label { Text = " 就绪 | 已发送: 0 | 接收: 0 | 丢失: 0 | 丢包率: 0%", Location = new Point(15, 485), Size = new Size(850, 28), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageSinglePing.Controls.Add(lblSingleFooter);
            AutoStretch(pageSinglePing, lblSingleFooter);
            AutoStickBottom(pageSinglePing, lblSingleFooter);
            AutoStretch(pageSinglePing, groupResult, width: true, height: true, rightMargin: 15, bottomMargin: lblSingleFooter.Height + 20);

            btnStartSingle.Click += async (s, e) => { await RunSinglePingAsync(); };
            btnStopSingle.Click += (s, e) => { ctsSingle?.Cancel(); };
        }

        private async Task RunSinglePingAsync()
        {
            string target = comboSingleTarget.Text.Trim();
            if (!int.TryParse(txtSingleCount.Text, out int count) || count <= 0) return;
            if (!int.TryParse(txtSingleSize.Text, out int size) || size <= 0 || size > 65000) return;

            btnStartSingle.Enabled = false; btnStopSingle.Enabled = true;
            txtSingleConsole.Clear();
            txtSingleConsole.AppendText($"正在 Ping {target} 具有 {size} 字节的数据:\r\n\r\n");

            ctsSingle = new CancellationTokenSource();
            int sent = 0, received = 0, lost = 0;
            byte[] buf = new byte[size];

            for (int i = 0; i < count; i++)
            {
                if (ctsSingle.Token.IsCancellationRequested) { txtSingleConsole.AppendText("\r\n⚠️ 用户终止。"); break; }
                sent++;
                lblSingleFooter.Text = $" 正在测试 | 已发送: {sent} | 接收: {received} | 丢失: {lost} | 丢包率: {(int)((double)lost / sent * 100)}%";
                try
                {
                    using (Ping p = new Ping())
                    {
                        PingReply reply = await p.SendPingAsync(target, 1000, buf, new PingOptions(64, true));
                        if (reply.Status == IPStatus.Success)
                        {
                            received++;
                            // 【修复】TTL(Options.Ttl) 是 IPv4 概念，Ping 到 IPv6 地址(比如 www.baidu.com 双栈解析出的 IPv6)
                            // 时 reply.Options 本身就是 null，之前直接格式化会显示成空白的"TTL="。这里做个兜底文案。
                            string ttlText = reply.Options != null ? reply.Options.Ttl.ToString() : "N/A(IPv6无此字段)";
                            txtSingleConsole.AppendText($"来自 {reply.Address} 的回复: 字节={size} 时间={reply.RoundtripTime}ms TTL={ttlText}\r\n");
                        }
                        else
                        {
                            lost++; txtSingleConsole.AppendText($"请求超时。\r\n");
                        }
                    }
                }
                catch { lost++; txtSingleConsole.AppendText($"请求异常中断。\r\n"); }
                try { await Task.Delay(1000, ctsSingle.Token); } catch { break; }
            }
            btnStartSingle.Enabled = true; btnStopSingle.Enabled = false;
            double loss = sent > 0 ? (double)lost / sent * 100 : 0;
            lblSingleFooter.Text = $" 诊断完成 | 已发送: {sent} | 接收: {received} | 丢失: {lost} | 丢包率: {(int)loss}%";
            txtSingleConsole.AppendText($"\r\n📊 {target} 的 Ping 统计信息:\r\n    数据包: 已发送 = {sent}，已接收 = {received}，丢失 = {lost} ({(int)loss}% 丢失)\r\n");
        }

        private void BuildKeepPingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " 🔄 持续Ping配置 ", Location = new Point(15, 15), Size = new Size(850, 120), ForeColor = colorCyan, Font = fontBold };
            pageKeepPing.Controls.Add(groupConfig);
            AutoStretch(pageKeepPing, groupConfig);

            Label lblTarget = new Label { Text = "目标主机:", Location = new Point(15, 32), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            comboKeepTarget = new ComboBox { Location = new Point(90, 28), Size = new Size(460, 25), Font = fontNormal };
            comboKeepTarget.Items.AddRange(new string[] { "223.5.5.5", "114.114.114.114", "www.baidu.com" });
            comboKeepTarget.Text = "223.5.5.5";

            Label lblSize = new Label { Text = "数据包大小:", Location = new Point(570, 32), Size = new Size(80, 20), Font = fontNormal, ForeColor = textDark };
            txtKeepSize = new TextBox { Text = "32", Location = new Point(655, 28), Size = new Size(80, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            Label lblInterval = new Label { Text = "间隔时间:", Location = new Point(15, 75), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            txtKeepInterval = new TextBox { Text = "1", Location = new Point(90, 71), Size = new Size(60, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblSec = new Label { Text = "秒", Location = new Point(155, 75), Size = new Size(25, 20), Font = fontNormal, ForeColor = Color.Gray };

            btnStartKeep = new Button { Text = "🔄 开始持续Ping", Location = new Point(200, 68), Size = new Size(130, 32), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite };
            btnStopKeep = new Button { Text = "🔲 停止", Location = new Point(340, 68), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnStopKeep.FlatAppearance.BorderColor = colorBorder; btnStopKeep.Enabled = false;
            btnExportKeep = new Button { Text = "💾 导出结果", Location = new Point(430, 68), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnExportKeep.FlatAppearance.BorderColor = colorBorder;
            btnClearKeep = new Button { Text = "🧹 清空结果", Location = new Point(530, 68), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnClearKeep.FlatAppearance.BorderColor = colorBorder;

            groupConfig.Controls.AddRange(new Control[] { lblTarget, comboKeepTarget, lblSize, txtKeepSize, lblInterval, txtKeepInterval, lblSec, btnStartKeep, btnStopKeep, btnExportKeep, btnClearKeep });

            GroupBox groupResult = new GroupBox { Text = " 🔄 持续Ping结果 ", Location = new Point(15, 145), Size = new Size(850, 330), ForeColor = colorCyan, Font = fontBold };
            pageKeepPing.Controls.Add(groupResult);

            Panel cyanBar = new Panel { Location = new Point(10, 22), Size = new Size(830, 6), BackColor = colorCyan };
            txtKeepConsole = new TextBox { Location = new Point(10, 28), Size = new Size(830, 290), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 10F), BackColor = colorWhite, ForeColor = textDark };
            groupResult.Controls.AddRange(new Control[] { cyanBar, txtKeepConsole });
            AutoStretch(groupResult, cyanBar);
            AutoStretch(groupResult, txtKeepConsole, width: true, height: true, rightMargin: 10, bottomMargin: 10);

            lblKeepFooter = new Label { Text = "就绪 | 已发送: 0 | 成功: 0 | 失败: 0 | 成功率: 0%", Location = new Point(15, 465), Size = new Size(850, 28), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageKeepPing.Controls.Add(lblKeepFooter);
            AutoStretch(pageKeepPing, lblKeepFooter);
            AutoStickBottom(pageKeepPing, lblKeepFooter);
            AutoStretch(pageKeepPing, groupResult, width: true, height: true, rightMargin: 15, bottomMargin: lblKeepFooter.Height + 20);

            btnStartKeep.Click += async (s, e) => { await RunKeepPingAsync(); };
            btnStopKeep.Click += (s, e) => { ctsKeep?.Cancel(); };
            btnClearKeep.Click += (s, e) => { txtKeepConsole.Clear(); keepSent = keepSuccess = keepFail = 0; lblKeepFooter.Text = "就绪 | 已发送: 0 | 成功: 0 | 失败: 0 | 成功率: 0%"; };
            btnExportKeep.Click += (s, e) => { SaveConsoleLog(txtKeepConsole.Text, "持续Ping结果"); };
        }

        private async Task RunKeepPingAsync()
        {
            string target = comboKeepTarget.Text.Trim();
            if (!int.TryParse(txtKeepSize.Text, out int size) || !int.TryParse(txtKeepInterval.Text, out int interval) || interval <= 0) return;
            btnStartKeep.Enabled = false; btnStopKeep.Enabled = true;
            ctsKeep = new CancellationTokenSource();
            byte[] buf = new byte[size];

            while (!ctsKeep.Token.IsCancellationRequested)
            {
                keepSent++;
                try
                {
                    using (Ping p = new Ping())
                    {
                        PingReply reply = await p.SendPingAsync(target, 1000, buf);
                        if (reply.Status == IPStatus.Success)
                        {
                            keepSuccess++;
                            txtKeepConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] 来自 {reply.Address}: 字节={size} 时间={reply.RoundtripTime}ms\r\n");
                        }
                        else
                        {
                            keepFail++; txtKeepConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] 探测超时。\r\n");
                        }
                    }
                }
                catch { keepFail++; txtKeepConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] 链路底层不可达。\r\n"); }
                double succRate = keepSent > 0 ? (double)keepSuccess / keepSent * 100 : 0;
                lblKeepFooter.Text = $"正在测试 | 已发送: {keepSent} | 成功: {keepSuccess} | 失败: {keepFail} | 成功率: {(int)succRate}%";
                try { await Task.Delay(interval * 1000, ctsKeep.Token); } catch { break; }
            }
            btnStartKeep.Enabled = true; btnStopKeep.Enabled = false;
        }

        private void BuildBatchPingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " 📦 批量Ping设置 ", Location = new Point(15, 10), Size = new Size(850, 160), ForeColor = colorCyan, Font = fontBold };
            pageBatchPing.Controls.Add(groupConfig);
            AutoStretch(pageBatchPing, groupConfig);

            Label lblListTitle = new Label { Text = "🖨️ 主机列表 (每行一个IP/域名):", Location = new Point(15, 22), Size = new Size(200, 18), Font = fontNormal, ForeColor = textDark };
            txtBatchList = new TextBox { Location = new Point(15, 42), Size = new Size(400, 105), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9.5F), Text = "223.5.5.5\r\n8.8.8.8\r\n114.114.114.114\r\nbaidu.com" };
            groupConfig.Controls.AddRange(new Control[] { lblListTitle, txtBatchList });

            Label lblT = new Label { Text = "⏱️ 超时(ms):", Location = new Point(430, 25), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtBatchTimeout = new TextBox { Text = "1500", Location = new Point(515, 22), Size = new Size(65, 23), Font = fontNormal };
            Label lblTh = new Label { Text = "⚡ 并发/线程:", Location = new Point(600, 25), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtBatchThreads = new TextBox { Text = "100", Location = new Point(690, 22), Size = new Size(65, 23), Font = fontNormal };

            Label lblM = new Label { Text = "🔄 模式:", Location = new Point(430, 60), Size = new Size(60, 20), Font = fontNormal, ForeColor = textDark };
            comboBatchMode = new ComboBox { Location = new Point(495, 57), Size = new Size(100, 25), Font = fontNormal };
            comboBatchMode.Items.Add("快速扫描"); comboBatchMode.Text = "快速扫描";
            Label lblI = new Label { Text = "⏱️ 间隔(s):", Location = new Point(610, 60), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            txtBatchInterval = new TextBox { Text = "5", Location = new Point(680, 57), Size = new Size(50, 23), Font = fontNormal };

            groupConfig.Controls.AddRange(new Control[] { lblT, txtBatchTimeout, lblTh, txtBatchThreads, lblM, comboBatchMode, lblI, txtBatchInterval });

            btnStartBatch = new Button { Text = "🚀 开始", Location = new Point(435, 105), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite };
            btnStopBatch = new Button { Text = "🔲 停止", Location = new Point(525, 105), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopBatch.FlatAppearance.BorderColor = colorBorder;
            btnExportBatch = new Button { Text = "💾 导出", Location = new Point(615, 105), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnExportBatch.FlatAppearance.BorderColor = colorBorder;
            btnClearBatch = new Button { Text = "🧹 清空", Location = new Point(705, 105), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnClearBatch.FlatAppearance.BorderColor = colorBorder;

            groupConfig.Controls.AddRange(new Control[] { btnStartBatch, btnStopBatch, btnExportBatch, btnClearBatch });

            listViewBatch = new ListView { Location = new Point(15, 180), Size = new Size(850, 295), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Segoe UI", 9F) };
            listViewBatch.Columns.AddRange(new ColumnHeader[] {
                new ColumnHeader { Text = "#", Width = 50 }, new ColumnHeader { Text = "主机/IP", Width = 140 }, new ColumnHeader { Text = "状态", Width = 80 },
                new ColumnHeader { Text = "延迟(ms)", Width = 80 }, new ColumnHeader { Text = "最小(ms)", Width = 80 }, new ColumnHeader { Text = "最大(ms)", Width = 80 },
                new ColumnHeader { Text = "丢包率", Width = 80 }, new ColumnHeader { Text = "测试时间", Width = 150 }
            });
            pageBatchPing.Controls.Add(listViewBatch);
            AutoFillLastColumn(listViewBatch, 120);

            lblBatchFooter = new Label { Text = "就绪 | 总: 0 | 在线: 0 | 离线: 0 | 成功率: 0% | 平均延迟: -", Location = new Point(15, 485), Size = new Size(850, 28), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageBatchPing.Controls.Add(lblBatchFooter);
            AutoStretch(pageBatchPing, lblBatchFooter);
            AutoStickBottom(pageBatchPing, lblBatchFooter);
            AutoStretch(pageBatchPing, listViewBatch, width: true, height: true, rightMargin: 15, bottomMargin: lblBatchFooter.Height + 20);

            btnStartBatch.Click += async (s, e) => { await RunBatchPingAsync(); };
            btnStopBatch.Click += (s, e) => ctsBatch?.Cancel();
            btnClearBatch.Click += (s, e) => { listViewBatch.Items.Clear(); lblBatchFooter.Text = "就绪 | 总: 0 | 在线: 0 | 离线: 0 | 成功率: 0% | 平均延迟: -"; };
            btnExportBatch.Click += (s, e) => { ExportListViewToCsv(listViewBatch, "批量Ping扫描结果"); };
        }

        private async Task RunBatchPingAsync()
        {
            string[] hosts = txtBatchList.Lines;
            if (hosts.Length == 0) return;

            btnStartBatch.Enabled = false;
            btnStopBatch.Enabled = true;
            listViewBatch.Items.Clear();
            ctsBatch = new CancellationTokenSource();

            int total = 0, online = 0, offline = 0;
            int idx = 1;
            listViewBatch.BeginUpdate();
            var tasks = new List<Task>();

            foreach (var h in hosts)
            {
                if (string.IsNullOrWhiteSpace(h)) continue;
                total++;
                string currentHost = h.Trim();
                var item = new ListViewItem(idx++.ToString());
                item.SubItems.AddRange(new string[] { currentHost, "检测中...", "—", "—", "—", "0%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                listViewBatch.Items.Add(item);

                tasks.Add(Task.Run(async () => {
                    if (ctsBatch.Token.IsCancellationRequested) return;
                    try
                    {
                        using (Ping p = new Ping())
                        {
                            PingReply reply = await p.SendPingAsync(currentHost, 1500);
                            this.Invoke(new Action(() => {
                                if (reply.Status == IPStatus.Success)
                                {
                                    online++;
                                    item.SubItems[2].Text = "在线";
                                    item.SubItems[3].Text = $"{reply.RoundtripTime}";
                                    item.SubItems[4].Text = $"{reply.RoundtripTime}";
                                    item.SubItems[5].Text = $"{reply.RoundtripTime}";
                                    item.ForeColor = colorAccentG;
                                }
                                else
                                {
                                    offline++;
                                    item.SubItems[2].Text = "离线";
                                    item.ForeColor = colorAccentR;
                                }
                                lblBatchFooter.Text = $"运行中 | 总: {total} | 在线: {online} | 离线: {offline} | 成功率: {(int)((double)online / total * 100)}% | 平均延迟: -";
                            }));
                        }
                    }
                    catch
                    {
                        this.Invoke(new Action(() => {
                            offline++;
                            item.SubItems[2].Text = "离线";
                            item.ForeColor = colorAccentR;
                        }));
                    }
                }));
            }
            listViewBatch.EndUpdate();

            await Task.WhenAll(tasks);
            btnStartBatch.Enabled = true;
            btnStopBatch.Enabled = false;
        }
        #endregion
    }
}
