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
        #region ====== 9. DHCP 服务器探测与嗅探 ======
        private void BuildTabDhcpLayout()
        {
            Panel banner = new Panel { Location = new Point(15, 12), Size = new Size(880, 45), BackColor = Color.LightYellow, BorderStyle = BorderStyle.FixedSingle };
            Label lblBanner = new Label { Text = "⚠️ 运行机制: 本工具采用底层套接字重组机制，可跨过系统独占，彻底阻断由于系统 DHCP 客户端独占而失败的问题。", Location = new Point(10, 12), Size = new Size(860, 20), Font = fontBold, ForeColor = Color.SaddleBrown };
            banner.Controls.Add(lblBanner);
            tabDhcp.Controls.Add(banner);
            AutoStretch(tabDhcp, banner);
            AutoStretch(banner, lblBanner);

            GroupBox groupCtrl = new GroupBox { Text = " 🛠️ DHCP 路由深度探测控制台 (本地端口独占重组修复版) ", Location = new Point(15, 70), Size = new Size(880, 85), Font = fontBold, ForeColor = colorPrimary };
            tabDhcp.Controls.Add(groupCtrl);
            AutoStretch(tabDhcp, groupCtrl);

            btnStartDhcp = new Button { Text = "🔍 开始检测", Location = new Point(20, 30), Size = new Size(130, 35), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite };
            btnStartDhcp.FlatAppearance.BorderSize = 0;

            btnStopDhcp = new Button { Text = "🔲 停止检测", Location = new Point(165, 30), Size = new Size(130, 35), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopDhcp.FlatAppearance.BorderColor = colorBorder;

            Button btnClearDhcp = new Button { Text = "🧹 清空结果", Location = new Point(310, 30), Size = new Size(130, 35), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnClearDhcp.FlatAppearance.BorderColor = colorBorder;

            lblDhcpStatus = new Label { Text = "检测到的 DHCP 服务器数量: 0      |      就绪", Location = new Point(460, 38), Size = new Size(400, 20), Font = fontBold, ForeColor = textDark };
            groupCtrl.Controls.AddRange(new Control[] { btnStartDhcp, btnStopDhcp, btnClearDhcp, lblDhcpStatus });

            logDhcp = new TextBox
            {
                Location = new Point(15, 170),
                Size = new Size(880, 400),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = colorConsoleBg,
                ForeColor = Color.White,
                Font = new Font("Consolas", 10F, FontStyle.Regular)
            };
            tabDhcp.Controls.Add(logDhcp);
            AutoStretch(tabDhcp, logDhcp, width: true, height: true, rightMargin: 15, bottomMargin: 15);

            btnStartDhcp.Click += async (s, e) => { await RunDhcpDetectionAsync(); };
            btnStopDhcp.Click += (s, e) => { ctsDhcp?.Cancel(); };
            btnClearDhcp.Click += (s, e) => { logDhcp.Clear(); dhcpDetectedCount = 0; lblDhcpStatus.Text = "检测到的 DHCP 服务器数量: 0      |      就绪"; };
        }

        private async Task RunDhcpDetectionAsync()
        {
            string localIp = GetActiveLocalIp();
            if (string.IsNullOrEmpty(localIp) || localIp.Equals("未连接网络"))
            {
                MessageBox.Show("未捕获到本地网卡的有效IP，请确保网卡已正常连接局域网！", "提示"); return;
            }

            btnStartDhcp.Enabled = false; btnStopDhcp.Enabled = true;
            logDhcp.Clear();
            dhcpDetectedCount = 0;
            lblDhcpStatus.Text = "检测到的 DHCP 服务器数量: 0      |      正在搜寻嗅探中...";

            logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}] [+] 开始突破系统端口壁垒，拉起 DHCP 全网探针广播扫描...\r\n");

            byte[] clientMac = GetMacAddressByIp(localIp);
            if (clientMac == null) clientMac = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };

            UdpClient udpClient = null;
            try
            {
                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                try
                {
                    udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 68));
                }
                catch
                {
                    logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}] ⚠️ 全局广域68端口被死锁，切换至精准本地IP适配重组通道绑定...\r\n");
                    udpClient.Client.Bind(new IPEndPoint(IPAddress.Parse(localIp), 68));
                }

                udpClient.EnableBroadcast = true;
                ctsDhcp = new CancellationTokenSource();
                byte[] discoverPacket = BuildDhcpDiscoverPacket(clientMac);
                IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, 67);

                var listenTask = Task.Run(async () => {
                    while (!ctsDhcp.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var res = await udpClient.ReceiveAsync();
                            byte[] buf = res.Buffer;
                            if (buf.Length > 240 && buf[0] == 0x02)
                            {
                                Interlocked.Increment(ref dhcpDetectedCount);
                                this.Invoke(new Action(() => ParseAndLogDhcpOffer(buf)));
                            }
                        }
                        catch { break; }
                    }
                });

                logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}] 🛰️ 特征向量 DHCP Discover 广播注入包就绪，正全力推向全链路...\r\n");
                await udpClient.SendAsync(discoverPacket, discoverPacket.Length, broadcastEP);
                logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}] ⏳ 广播投递成功。进入 3.5 秒黄金监听视窗期抓取回执包...\r\n");

                await Task.Delay(3500);
                ctsDhcp.Cancel();
                udpClient.Close();
                await listenTask;

                logDhcp.AppendText($"============================================================\r\n");
                logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}] DHCP 深度空间探测扫描闭环完成。\r\n");
                logDhcp.AppendText($"============================================================\r\n");
                lblDhcpStatus.Text = $"检测到的 DHCP 服务器数量: {dhcpDetectedCount}      |      扫描完毕";
            }
            catch (Exception ex)
            {
                logDhcp.AppendText($"❌ 异常中断: {ex.Message}\r\n");
                lblDhcpStatus.Text = "检测失败 | 物理层套接字独占异常";
                if (udpClient != null) udpClient.Close();
            }
            finally
            {
                btnStartDhcp.Enabled = true; btnStopDhcp.Enabled = false;
            }
        }

        private byte[] BuildDhcpDiscoverPacket(byte[] clientMac)
        {
            byte[] packet = new byte[300];
            packet[0] = 0x01; packet[1] = 0x01; packet[2] = 0x06;
            Random rand = new Random();
            byte[] xid = new byte[4]; rand.NextBytes(xid);
            Array.Copy(xid, 0, packet, 4, 4);
            packet[10] = 0x80;
            Array.Copy(clientMac, 0, packet, 28, 6);
            packet[236] = 0x63; packet[237] = 0x82; packet[238] = 0x53; packet[239] = 0x63;
            packet[240] = 53; packet[241] = 1; packet[242] = 1;
            packet[243] = 55; packet[244] = 4; packet[245] = 1; packet[246] = 3; packet[247] = 6; packet[248] = 51;
            packet[249] = 255;
            return packet;
        }

        private void ParseAndLogDhcpOffer(byte[] buffer)
        {
            string offeredIp = $"{buffer[16]}.{buffer[17]}.{buffer[18]}.{buffer[19]}";
            string serverIp = "未知"; string mask = "未知"; string gateway = "未知"; string lease = "未知";
            List<string> dns = new List<string>();

            int ptr = 240;
            while (ptr + 1 < buffer.Length)
            {
                byte opt = buffer[ptr];
                if (opt == 255) break;
                if (opt == 0) { ptr++; continue; }

                byte len = buffer[ptr + 1];
                if (ptr + 2 + len > buffer.Length) break;

                switch (opt)
                {
                    case 1: if (len >= 4) mask = $"{buffer[ptr + 2]}.{buffer[ptr + 3]}.{buffer[ptr + 4]}.{buffer[ptr + 5]}"; break;
                    case 3: if (len >= 4) gateway = $"{buffer[ptr + 2]}.{buffer[ptr + 3]}.{buffer[ptr + 4]}.{buffer[ptr + 5]}"; break;
                    case 6:
                        for (int m = 0; m + 3 < len; m += 4)
                            dns.Add($"{buffer[ptr + 2 + m]}.{buffer[ptr + 3 + m]}.{buffer[ptr + 4 + m]}.{buffer[ptr + 5 + m]}");
                        break;
                    case 54: if (len >= 4) serverIp = $"{buffer[ptr + 2]}.{buffer[ptr + 3]}.{buffer[ptr + 4]}.{buffer[ptr + 5]}"; break;
                    case 51:
                        if (len >= 4)
                        {
                            uint sec = (uint)((buffer[ptr + 2] << 24) | (buffer[ptr + 3] << 16) | (buffer[ptr + 4] << 8) | buffer[ptr + 5]);
                            lease = $"{sec} 秒 (大约 {sec / 3600} 小时)";
                        }
                        break;
                }
                ptr += 2 + len;
            }

            if (serverIp == "未知") return;

            string macStr = GetMacViaArp(serverIp);
            string vendor = GetVendorName(macStr);

            logDhcp.AppendText($"\r\n[{DateTime.Now:HH:mm:ss}] 🚨 【捕获成功】检测到活动的 DHCP 服务源: {serverIp}\r\n");
            logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}]     硬件 MAC 地址: {macStr}\r\n");
            logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}]     网卡厂商归属: {vendor}\r\n");
            logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}]     意向分配 IP 地址 (Offered IP): {offeredIp}\r\n");
            logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}]     给出的掩码 (Subnet Mask): {mask}\r\n");
            logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}]     网关中继 (Gateway Address): {gateway}\r\n");
            logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}]     推送的 DNS 服务器: {string.Join(", ", dns)}\r\n");
            logDhcp.AppendText($"[{DateTime.Now:HH:mm:ss}]     租约时效时长 (Lease Time): {lease}\r\n");
        }
        #endregion
    }
}
