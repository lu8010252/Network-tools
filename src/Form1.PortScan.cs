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
        #region ====== 7. 高并发端口快速扫描器 ======
        private void BuildTabPortScanLayout()
        {
            GroupBox groupCtrl = new GroupBox { Text = " 🔍 TCP高并发端口快速扫描器 ", Location = new Point(15, 15), Size = new Size(880, 115), Font = fontBold, ForeColor = colorPrimary };
            tabPortScan.Controls.Add(groupCtrl);
            AutoStretch(tabPortScan, groupCtrl);

            Label lblTarget = new Label { Text = "目标IP/主机:", Location = new Point(15, 30), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtPortTarget = new TextBox { Text = "127.0.0.1", Location = new Point(105, 26), Size = new Size(180, 25), Font = fontNormal };

            Label lblRange = new Label { Text = "扫描端口范围:", Location = new Point(300, 30), Size = new Size(90, 20), Font = fontNormal, ForeColor = textDark };
            txtPortRange = new TextBox { Text = "21,22,23,25,80,135,139,443,445,1433,3306,3389,8080", Location = new Point(395, 26), Size = new Size(470, 25), Font = fontNormal };
            groupCtrl.Controls.AddRange(new Control[] { lblTarget, txtPortTarget, lblRange, txtPortRange });
            AutoStretch(groupCtrl, txtPortRange);

            Label lblTh = new Label { Text = "并发线程/频率:", Location = new Point(15, 72), Size = new Size(90, 20), Font = fontNormal, ForeColor = textDark };
            txtPortThreads = new TextBox { Text = "100", Location = new Point(105, 68), Size = new Size(60, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            btnStartPortScan = new Button { Text = "🚀 爆破扫描", Location = new Point(190, 65), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite };
            btnStartPortScan.FlatAppearance.BorderSize = 0;
            btnStopPortScan = new Button { Text = "🔲 停止", Location = new Point(300, 65), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopPortScan.FlatAppearance.BorderColor = colorBorder;
            btnExportPortScan = new Button { Text = "💾 导出资产", Location = new Point(390, 65), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnExportPortScan.FlatAppearance.BorderColor = colorBorder;

            progressBarPorts = new ProgressBar { Location = new Point(495, 72), Size = new Size(370, 20), Style = ProgressBarStyle.Continuous };
            groupCtrl.Controls.AddRange(new Control[] { lblTh, txtPortThreads, btnStartPortScan, btnStopPortScan, btnExportPortScan, progressBarPorts });
            AutoStretch(groupCtrl, progressBarPorts);

            listViewPorts = new ListView { Location = new Point(15, 145), Size = new Size(880, 400), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Segoe UI", 9.5F) };
            listViewPorts.Columns.AddRange(new ColumnHeader[] { new ColumnHeader { Text = "目标端口 (Port)", Width = 120 }, new ColumnHeader { Text = "当前开放状态", Width = 180 }, new ColumnHeader { Text = "推测标准已知服务", Width = 260 }, new ColumnHeader { Text = "审计扫描时刻", Width = 200 } });
            tabPortScan.Controls.Add(listViewPorts);
            AutoFillLastColumn(listViewPorts, 180);

            lblPortFooter = new Label { Text = "就绪 | 等待开启端口深度审计...", Location = new Point(15, 555), Size = new Size(880, 26), BackColor = Color.FromArgb(242, 246, 255), TextAlign = ContentAlignment.MiddleLeft };
            tabPortScan.Controls.Add(lblPortFooter);
            AutoStretch(tabPortScan, lblPortFooter);
            AutoStickBottom(tabPortScan, lblPortFooter);
            AutoStretch(tabPortScan, listViewPorts, width: true, height: true, rightMargin: 15, bottomMargin: lblPortFooter.Height + 20);

            btnStartPortScan.Click += async (s, e) => { await RunPortScanAsync(); };
            btnStopPortScan.Click += (s, e) => { ctsPortScan?.Cancel(); };
            btnExportPortScan.Click += (s, e) => { ExportListViewToCsv(listViewPorts, "开放端口审计报表"); };
        }

        private async Task RunPortScanAsync()
        {
            string host = txtPortTarget.Text.Trim();
            if (string.IsNullOrEmpty(host)) return;

            List<int> targetPorts = new List<int>();
            try
            {
                string[] parts = txtPortRange.Text.Split(',');
                foreach (var p in parts)
                {
                    if (p.Contains("-"))
                    {
                        string[] rng = p.Split('-');
                        int start = int.Parse(rng[0]); int end = int.Parse(rng[1]);
                        for (int k = start; k <= end; k++) targetPorts.Add(k);
                    }
                    else
                    {
                        targetPorts.Add(int.Parse(p));
                    }
                }
            }
            catch { MessageBox.Show("端口范围语法格式编写有误！支持 80,443 或 21-1024 混合格式。", "规范格式提示"); return; }

            if (targetPorts.Count == 0) return;
            if (!int.TryParse(txtPortThreads.Text, out int maxThreads) || maxThreads <= 0) maxThreads = 50;

            btnStartPortScan.Enabled = false; btnStopPortScan.Enabled = true;
            listViewPorts.Items.Clear(); progressBarPorts.Value = 0;
            ctsPortScan = new CancellationTokenSource();

            int checkedCount = 0; int openedCount = 0;
            var tasks = new List<Task>();
            // 跟局域网发现页同样的修复：不再是每测完一个端口就单独跨线程 Add 一次，
            // 改成先收集到线程安全字典里，全部测完后一次性 AddRange，避免 ListView 滚动条失步。
            var resultsByPort = new System.Collections.Concurrent.ConcurrentDictionary<int, ListViewItem>();

            using (var sem = new SemaphoreSlim(maxThreads))
            {
                foreach (int port in targetPorts)
                {
                    if (ctsPortScan.Token.IsCancellationRequested) break;
                    await sem.WaitAsync();
                    int currentPort = port;

                    tasks.Add(Task.Run(async () => {
                        bool isOpen = false;
                        try
                        {
                            using (TcpClient client = new TcpClient())
                            {
                                var connectTask = client.ConnectAsync(host, currentPort);
                                if (await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask)
                                {
                                    if (client.Connected) isOpen = true;
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            sem.Release();
                            Interlocked.Increment(ref checkedCount);
                            if (isOpen)
                            {
                                Interlocked.Increment(ref openedCount);
                                string svc = GetPlaceholderService(currentPort);
                                var item = new ListViewItem(currentPort.ToString());
                                item.SubItems.AddRange(new string[] { "开放 (Open/Listening)", svc, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                                item.ForeColor = colorAccentG;
                                resultsByPort[currentPort] = item;
                            }
                            this.Invoke(new Action(() => {
                                progressBarPorts.Value = Math.Min(100, (int)((double)checkedCount / targetPorts.Count * 100));
                                lblPortFooter.Text = $" 进度: {checkedCount}/{targetPorts.Count} | 开放总数: {openedCount} 个开放端口";
                            }));
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            var sortedPortItems = resultsByPort.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
            listViewPorts.BeginUpdate();
            listViewPorts.Items.Clear();
            listViewPorts.Items.AddRange(sortedPortItems);
            listViewPorts.EndUpdate();

            btnStartPortScan.Enabled = true; btnStopPortScan.Enabled = false;
            MessageBox.Show($"开放端口深度摸底审计完成！共发现 {openedCount} 个活动通信端口。", "审计完成");
        }

        private string GetPlaceholderService(int port)
        {
            switch (port)
            {
                case 21: return "FTP 文件传输服务";
                case 22: return "SSH 安全外壳协议远程协议";
                case 23: return "Telnet 明文远程终端";
                case 25: return "SMTP 简单邮件路由协议";
                case 80: return "HTTP 基础万维网开放数据流";
                case 135: return "RPC 微软远程过程调用基础";
                case 139: return "NetBIOS 局域网共享映射基础";
                case 443: return "HTTPS 安全高阶加密网页协议";
                case 445: return "SMB 微软底层高级共享风暴缺陷";
                case 1433: return "Microsoft SQL Server 数据库";
                case 3306: return "MySQL 开放通用数据库存储";
                case 3389: return "RDP 微软官方图形远程桌面";
                case 8080: return "Apache/Tomcat 经典高阶代理容器";
                default: return "未知自建/特殊闭源私有应用流";
            }
        }
        #endregion
    }
}
