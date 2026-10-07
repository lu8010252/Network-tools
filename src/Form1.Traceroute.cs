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
        #region ====== 8. 可视化全路由追踪溯源 ======
        private void BuildTabTracerouteLayout()
        {
            GroupBox groupCtrl = new GroupBox { Text = " 🧭 多中继节点路由追踪及链路耗时溯源控制台 ", Location = new Point(15, 15), Size = new Size(880, 150), Font = fontBold, ForeColor = colorPrimary };
            tabTraceroute.Controls.Add(groupCtrl);
            AutoStretch(tabTraceroute, groupCtrl);

            int gap = 8;
            int yLbl = 55, yBox = 51, yBtn = 95;

            // 【彻底修复：路由追踪按钮被裁切】之前反复调过间距，也加过"页面切换时强制重算"，
            // 但只要"参数输入框 + 两个按钮"全挤在一行，某些高 DPI 缩放倍数下这一行需要的
            // 总宽度就是会比设计给的空间更宽——不管怎么抠像素、怎么优化触发时机都治标不治本。
            // 这次不跟像素较劲了，把两个按钮挪到单独一行（输入框们一行，按钮另起一行），
            // 从结构上让这一行不可能再因为宽度不够被裁，不用再猜任何 DPI 缩放倍数。
            Label lblT = new Label { Text = "探测总目标:", AutoSize = true, Location = new Point(15, yLbl), Font = fontNormal, ForeColor = textDark };
            groupCtrl.Controls.Add(lblT);
            txtTraceTarget = new TextBox { Text = "www.baidu.com", Location = new Point(lblT.Right + gap, yBox), Size = new Size(220, 25), Font = fontNormal };
            groupCtrl.Controls.Add(txtTraceTarget);

            Label lblM = new Label { Text = "Max跳数(TTL):", AutoSize = true, Location = new Point(txtTraceTarget.Right + gap * 2, yLbl), Font = fontNormal, ForeColor = textDark };
            groupCtrl.Controls.Add(lblM);
            txtTraceMaxHops = new TextBox { Text = "30", Location = new Point(lblM.Right + gap, yBox), Size = new Size(50, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            groupCtrl.Controls.Add(txtTraceMaxHops);

            Label lblTo = new Label { Text = "阈限超时(ms):", AutoSize = true, Location = new Point(txtTraceMaxHops.Right + gap * 2, yLbl), Font = fontNormal, ForeColor = textDark };
            groupCtrl.Controls.Add(lblTo);
            txtTraceTimeout = new TextBox { Text = "1200", Location = new Point(lblTo.Right + gap, yBox), Size = new Size(60, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            groupCtrl.Controls.Add(txtTraceTimeout);

            btnStartTrace = new Button { Text = "🚀 路径追踪", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 0, 14, 0), Location = new Point(15, yBtn), Height = 32, FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite };
            btnStartTrace.FlatAppearance.BorderSize = 0;
            groupCtrl.Controls.Add(btnStartTrace);

            btnStopTrace = new Button { Text = "🔲 中断", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 0, 14, 0), Location = new Point(btnStartTrace.Right + gap, yBtn), Height = 32, FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopTrace.FlatAppearance.BorderColor = colorBorder;
            groupCtrl.Controls.Add(btnStopTrace);

            listViewTrace = new ListView { Location = new Point(15, 180), Size = new Size(880, 400), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Consolas", 9.5F) };
            listViewTrace.Columns.AddRange(new ColumnHeader[] { new ColumnHeader { Text = "跃点 (Hop)", Width = 80 }, new ColumnHeader { Text = "中间节点路由交换机 IP", Width = 240 }, new ColumnHeader { Text = "往返耗时 (RTT延迟)", Width = 200 }, new ColumnHeader { Text = "网络质量诊断结论", Width = 220 } });
            tabTraceroute.Controls.Add(listViewTrace);
            AutoFillLastColumn(listViewTrace, 180);

            lblTraceFooter = new Label { Text = "就绪 | 等待开启 TTL 衰减拓扑追踪...", Location = new Point(15, 590), Size = new Size(880, 26), BackColor = Color.FromArgb(242, 246, 255), TextAlign = ContentAlignment.MiddleLeft };
            tabTraceroute.Controls.Add(lblTraceFooter);
            AutoStretch(tabTraceroute, lblTraceFooter);
            AutoStickBottom(tabTraceroute, lblTraceFooter);
            AutoStretch(tabTraceroute, listViewTrace, width: true, height: true, rightMargin: 15, bottomMargin: lblTraceFooter.Height + 20);

            btnStartTrace.Click += async (s, e) => { await RunTracerouteAsync(); };
            btnStopTrace.Click += (s, e) => ctsTrace?.Cancel();
        }

        private async Task RunTracerouteAsync()
        {
            string target = txtTraceTarget.Text.Trim();
            if (string.IsNullOrEmpty(target)) return;
            if (!int.TryParse(txtTraceMaxHops.Text, out int maxHops) || maxHops <= 0 || maxHops > 128) maxHops = 30;
            if (!int.TryParse(txtTraceTimeout.Text, out int timeout) || timeout <= 0) timeout = 1200;

            btnStartTrace.Enabled = false; btnStopTrace.Enabled = true;
            listViewTrace.Items.Clear();
            lblTraceFooter.Text = "核心路径测绘计算展开中...";
            ctsTrace = new CancellationTokenSource();
            txtTraceTarget.Enabled = false;

            _ = Task.Run(async () => {
                try
                {
                    byte[] buffer = new byte[32];
                    using (Ping ping = new Ping())
                    {
                        for (int ttl = 1; ttl <= maxHops; ttl++)
                        {
                            if (ctsTrace.Token.IsCancellationRequested)
                            {
                                this.Invoke(new Action(() => {
                                    var it = new ListViewItem("*"); it.SubItems.AddRange(new string[] { "⚠️ 用户强行终止", "—", "手动打断" });
                                    listViewTrace.Items.Add(it);
                                }));
                                break;
                            }

                            PingOptions opt = new PingOptions(ttl, true);
                            Stopwatch sw = Stopwatch.StartNew();
                            PingReply reply = await ping.SendPingAsync(target, timeout, buffer, opt);
                            sw.Stop();

                            bool finished = false;
                            this.Invoke(new Action(() => {
                                var item = new ListViewItem(ttl.ToString());
                                if (reply.Status == IPStatus.TtlExpired)
                                {
                                    item.SubItems.AddRange(new string[] { reply.Address.ToString(), $"{sw.ElapsedMilliseconds} ms", "中继节点转发正常" });
                                    item.ForeColor = textDark;
                                }
                                else if (reply.Status == IPStatus.Success)
                                {
                                    item.SubItems.AddRange(new string[] { reply.Address.ToString(), $"{sw.ElapsedMilliseconds} ms", "🏁 已成功抵达骨干终点主机" });
                                    item.ForeColor = colorAccentG; item.Font = fontBold;
                                    finished = true;
                                }
                                else
                                {
                                    item.SubItems.AddRange(new string[] { " * * * ", "请求超时", "💣 该路由器节点严限ICMP或拒绝回执" });
                                    item.ForeColor = colorAccentR;
                                }
                                listViewTrace.Items.Add(item);
                                lblTraceFooter.Text = $"正在追踪第 {ttl} 跳节点...";
                            }));

                            if (finished) break;
                            await Task.Delay(200);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() => MessageBox.Show($"路由链路审计发生致命内部异常: {ex.Message}")));
                }
                finally
                {
                    this.Invoke(new Action(() => {
                        btnStartTrace.Enabled = true; btnStopTrace.Enabled = false;
                        txtTraceTarget.Enabled = true;
                        lblTraceFooter.Text = "路由追踪溯源工作链正常完结。";
                    }));
                }
            });
        }
        #endregion
    }
}
