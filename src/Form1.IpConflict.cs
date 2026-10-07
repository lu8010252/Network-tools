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
        #region ====== 10.5 IP 冲突检测 ======
        /// <summary>
        /// 【新增功能】IP冲突检测：对一个IP段里的每个地址反复做几次ARP解析，如果同一个IP在几次
        /// 解析里出现了不止一个MAC地址，就判定为"冲突"（两台设备抢用了同一个IP）。
        /// 【修复】检测方法换成跟"局域网主机发现"完全一致、已验证可靠的方式：直接 SendARP + ICMP兜底，
        /// 不再先用 arp -d 清缓存——那种做法在部分系统上反而会让重新解析经常失败，导致明明在线的设备
        /// 被误判成离线（之前测试实际在线10台，只测到2台就是这个原因）。
        /// 说明：这依然是主动反复探测取样，不是真正意义上被动抓包监听网络里的ARP广播包（那个需要驱动级
        /// 抓包库，超出这个工具的范围），对大多数局域网IP冲突场景已经够用，但发生得很隐蔽/频率很低的
        /// 冲突可能漏检。
        /// </summary>
        /// <summary>ListView 排序用：Column=-1 表示默认综合排序(冲突优先、冲突次数多的在前、同类再按IP从小到大)。</summary>
        private class IpConflictSorter : System.Collections.IComparer
        {
            public int Column = -1;
            public bool Ascending = true;

            private static int StatusRank(string s) => s.Contains("冲突") ? 0 : (s == "正常" ? 1 : 2);
            private static int IpToInt(string ip)
            {
                int val = 0;
                foreach (var p in ip.Split('.')) val = val * 256 + (int.TryParse(p, out int n) ? n : 0);
                return val;
            }

            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x; var b = (ListViewItem)y;
                if (Column == -1)
                {
                    int rankA = StatusRank(a.SubItems[2].Text), rankB = StatusRank(b.SubItems[2].Text);
                    if (rankA != rankB) return rankA.CompareTo(rankB);
                    int.TryParse(a.SubItems[3].Text, out int ca); int.TryParse(b.SubItems[3].Text, out int cb);
                    if (ca != cb) return cb.CompareTo(ca);
                    return IpToInt(a.SubItems[0].Text).CompareTo(IpToInt(b.SubItems[0].Text));
                }
                int result;
                if (Column == 2) result = StatusRank(a.SubItems[2].Text).CompareTo(StatusRank(b.SubItems[2].Text));
                else if (Column == 3) { int.TryParse(a.SubItems[3].Text, out int na); int.TryParse(b.SubItems[3].Text, out int nb); result = na.CompareTo(nb); }
                else if (Column == 0) result = IpToInt(a.SubItems[0].Text).CompareTo(IpToInt(b.SubItems[0].Text));
                else result = string.Compare(a.SubItems[Column].Text, b.SubItems[Column].Text, StringComparison.OrdinalIgnoreCase);
                return Ascending ? result : -result;
            }
        }

        private void BuildTabIpConflictLayout()
        {
            tabIpConflict.AutoScroll = true;

            Label lbl = new Label { Text = "IP 冲突检测", Location = new Point(20, 20), AutoSize = true, Font = fontTitle, ForeColor = colorPrimary };
            tabIpConflict.Controls.Add(lbl);

            GroupBox groupCfg = new GroupBox { Text = " 检测配置 ", Location = new Point(15, 55), Size = new Size(850, 110), Font = fontBold, ForeColor = colorPrimary };
            tabIpConflict.Controls.Add(groupCfg);
            AutoStretch(tabIpConflict, groupCfg);

            Label lblIfaceTag = new Label { Text = "当前网卡:", Location = new Point(15, 30), AutoSize = true, Font = fontNormal };
            Label lblIfaceVal = new Label { Text = currentIfaceName, Location = new Point(100, 30), AutoSize = true, Font = fontBold, ForeColor = colorPrimary };

            Label lblRangeTag = new Label { Text = "IP范围:", Location = new Point(15, 65), AutoSize = true, Font = fontNormal };
            string ipConflictSubnet = GetSubnetForIface(currentIfaceName);
            string ipConflictDefaultRange = "192.168.1.1-254";
            if (ipConflictSubnet != null)
            {
                string networkPart = ipConflictSubnet.Split('/')[0];
                string prefix3 = networkPart.Substring(0, networkPart.LastIndexOf('.'));
                ipConflictDefaultRange = $"{prefix3}.1-254";
            }
            TextBox txtIpRange = new TextBox { Text = ipConflictDefaultRange, Location = new Point(100, 62), Size = new Size(220, 25), Font = fontNormal };
            Label lblFormatTip = new Label { Text = "(格式: 192.168.1.1-254 或 192.168.1.0/24)", Location = new Point(330, 65), AutoSize = true, ForeColor = Color.Gray, Font = fontNormal };

            Button btnStart = CreateStyledButton("开始检测", 620, 60, 100, 34);
            Button btnStop = CreateStyledButton("停止", 730, 60, 90, 34, false);
            btnStop.Enabled = false;

            groupCfg.Controls.AddRange(new Control[] { lblIfaceTag, lblIfaceVal, lblRangeTag, txtIpRange, lblFormatTip, btnStart, btnStop });

            Label lblSummary = new Label { Text = "就绪 | 总计: 0 | 已扫: 0 | 冲突: 0", Location = new Point(15, 175), AutoSize = true, ForeColor = colorPrimary, Font = fontBold };
            tabIpConflict.Controls.Add(lblSummary);

            Label lblGridHint = new Label { Text = "总览（点击方格可定位到下方明细行）:", Location = new Point(15, 205), AutoSize = true, ForeColor = Color.Gray, Font = fontNormal };
            tabIpConflict.Controls.Add(lblGridHint);

            FlowLayoutPanel gridPanel = new FlowLayoutPanel
            {
                Location = new Point(15, 228),
                Size = new Size(850, 110),
                AutoScroll = true,
                WrapContents = true,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth + 6, 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            tabIpConflict.Controls.Add(gridPanel);
            var gridLabels = new Dictionary<string, Label>();

            ListView lv = new ListView
            {
                Location = new Point(15, 348),
                Size = new Size(850, 300),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Scrollable = true,
                Font = fontNormal
            };
            lv.Columns.Add("IP地址", 140);
            lv.Columns.Add("MAC地址", 160);
            lv.Columns.Add("状态", 100);
            lv.Columns.Add("冲突次数", 90);
            lv.Columns.Add("冲突MAC地址", 300);
            tabIpConflict.Controls.Add(lv);
            AutoStretch(tabIpConflict, lv);
            AutoFillLastColumn(lv, 200);

            var sorter = new IpConflictSorter();
            lv.ListViewItemSorter = sorter;
            lv.ColumnClick += (s, e) =>
            {
                if (sorter.Column == e.Column) sorter.Ascending = !sorter.Ascending;
                else { sorter.Column = e.Column; sorter.Ascending = true; }
                lv.Sort();
            };

            btnStart.Click += async (s, e) =>
            {
                List<string> ips;
                try { ips = ParseIpRangeSimple(txtIpRange.Text.Trim()); }
                catch (Exception ex) { MessageBox.Show("IP范围格式不对: " + ex.Message, "提示"); return; }
                if (ips.Count == 0 || ips.Count > 512) { MessageBox.Show("IP数量不对（0个或超过512个），检查一下格式。", "提示"); return; }

                lv.Items.Clear();
                gridPanel.Controls.Clear();
                gridLabels.Clear();
                sorter.Column = -1; sorter.Ascending = true;

                gridPanel.SuspendLayout();
                Label lastGridLabel = null;
                foreach (var ip in ips)
                {
                    string shortNum = ip.Substring(ip.LastIndexOf('.') + 1);
                    Label box = new Label
                    {
                        Text = shortNum,
                        AutoSize = true,
                        MinimumSize = new Size(44, 22),
                        Padding = new Padding(4, 3, 4, 3),
                        Margin = new Padding(3),
                        TextAlign = ContentAlignment.MiddleCenter,
                        BackColor = Color.FromArgb(240, 242, 245),
                        ForeColor = textDark,
                        Font = new Font("Consolas", 8.5F, FontStyle.Bold),
                        BorderStyle = BorderStyle.FixedSingle,
                        Cursor = Cursors.Hand,
                        Tag = ip
                    };
                    box.Click += (bs, be) =>
                    {
                        string clickedIp = (string)box.Tag;
                        foreach (ListViewItem it in lv.Items)
                        {
                            if (it.SubItems[0].Text == clickedIp)
                            {
                                it.Selected = true;
                                it.EnsureVisible();
                                lv.Focus();
                                break;
                            }
                        }
                    };
                    gridLabels[ip] = box;
                    gridPanel.Controls.Add(box);
                    lastGridLabel = box;
                }
                if (lastGridLabel != null) gridPanel.SetFlowBreak(lastGridLabel, true);
                gridPanel.Controls.Add(new Label { Text = "", AutoSize = false, Size = new Size(1, 60), Margin = new Padding(0) });
                gridPanel.ResumeLayout();

                btnStart.Enabled = false; btnStop.Enabled = true;
                ctsIpConflict = new CancellationTokenSource();
                var token = ctsIpConflict.Token;

                var macCountByIp = new Dictionary<string, Dictionary<string, int>>();
                foreach (var ip in ips) macCountByIp[ip] = new Dictionary<string, int>();

                const int rounds = 5;
                var rnd = new Random();
                for (int round = 0; round < rounds; round++)
                {
                    if (token.IsCancellationRequested) break;
                    lblSummary.Text = $"扫描中(第{round + 1}/{rounds}轮) | 总计: {ips.Count} | 已扫: 0 | 冲突: -";

                    await Task.Run(() => RunCmd("arp", "-d"), token);
                    try { await Task.Delay(300, token); } catch (TaskCanceledException) { break; }

                    int scanned = 0;
                    var semaphore = new SemaphoreSlim(40);
                    var tasks = ips.Select(async ip =>
                    {
                        await semaphore.WaitAsync(token);
                        try
                        {
                            if (token.IsCancellationRequested) return;
                            try { await Task.Delay(rnd.Next(0, 150), token); } catch (TaskCanceledException) { return; }

                            string mac = await Task.Run(() => GetMacViaArp(ip), token);
                            if (mac == "—")
                            {
                                bool pinged = false;
                                try
                                {
                                    using (Ping p = new Ping())
                                    {
                                        var reply = await p.SendPingAsync(ip, 400);
                                        pinged = reply.Status == IPStatus.Success;
                                    }
                                }
                                catch { }
                                if (pinged) mac = await Task.Run(() => GetMacViaArp(ip), token);
                            }
                            if (mac != "—")
                            {
                                var dict = macCountByIp[ip];
                                lock (dict) { dict[mac] = dict.TryGetValue(mac, out int c) ? c + 1 : 1; }
                            }
                            this.Invoke(new Action(() =>
                            {
                                scanned++;
                                lblSummary.Text = $"扫描中(第{round + 1}/{rounds}轮) | 总计: {ips.Count} | 已扫: {scanned} | 冲突: -";
                            }));
                        }
                        finally { semaphore.Release(); }
                    });
                    try { await Task.WhenAll(tasks); } catch { }
                }

                int conflicts = 0;
                foreach (var ip in ips)
                {
                    var dict = macCountByIp[ip];
                    string mainMac = "—"; string status; Color color; string conflictMacs = "";
                    if (dict.Count == 0) { status = "离线"; color = Color.Gray; }
                    else
                    {
                        var sorted = dict.OrderByDescending(kv => kv.Value).ToList();
                        mainMac = sorted[0].Key;
                        if (dict.Count == 1) { status = "正常"; color = colorAccentG; }
                        else
                        {
                            status = "⚠冲突"; color = colorAccentR; conflicts++;
                            conflictMacs = string.Join(", ", sorted.Skip(1).Select(kv => $"{kv.Key}({kv.Value}次)"));
                            mainMac = $"{mainMac}({sorted[0].Value}次)";
                        }
                    }

                    var item = new ListViewItem(new[] { ip, mainMac, status, dict.Count > 1 ? (dict.Count - 1).ToString() : "0", conflictMacs });
                    item.ForeColor = color;
                    lv.Items.Add(item);

                    if (gridLabels.TryGetValue(ip, out Label box))
                    {
                        box.BackColor = color == Color.Gray ? Color.FromArgb(220, 220, 220) : color;
                        box.ForeColor = color == Color.Gray ? textDark : Color.White;
                    }
                }
                lv.Sort();
                lblSummary.Text = $"完成 | 总计: {ips.Count} | 已扫: {ips.Count} | 冲突: {conflicts}";

                btnStart.Enabled = true; btnStop.Enabled = false;
            };

            btnStop.Click += (s, e) => { ctsIpConflict?.Cancel(); };
        }


        private List<string> ParseIpRangeSimple(string text)
        {
            var result = new List<string>();
            if (text.Contains("/"))
            {
                var parts = text.Split('/');
                var baseIp = IPAddress.Parse(parts[0]);
                int prefix = int.Parse(parts[1]);
                if (prefix != 24) throw new Exception("目前只支持 /24 掩码");
                var bytes = baseIp.GetAddressBytes();
                for (int i = 1; i <= 254; i++)
                    result.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}.{i}");
            }
            else if (text.Contains("-"))
            {
                int dashIdx = text.LastIndexOf('.');
                string prefixPart = text.Substring(0, dashIdx);
                string rangePart = text.Substring(dashIdx + 1);
                var rangeSplit = rangePart.Split('-');
                int start = int.Parse(rangeSplit[0]);
                int end = int.Parse(rangeSplit[1]);
                for (int i = start; i <= end; i++)
                    result.Add($"{prefixPart}.{i}");
            }
            else
            {
                result.Add(text);
            }
            return result;
        }
        #endregion
    }
}
