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
        #region ====== 10. 局域网物理回路环路审计 ======
        private void BuildTabLoopLayout()
        {
            GroupBox groupCtrl = new GroupBox { Text = " 🔄 交换机物理回路（环路引发广播风暴）审计面板 ", Location = new Point(15, 15), Size = new Size(880, 85), Font = fontBold, ForeColor = colorPrimary };
            tabLoop.Controls.Add(groupCtrl);
            AutoStretch(tabLoop, groupCtrl);

            btnStartLoop = new Button { Text = "🚀 开始测试", Location = new Point(20, 30), Size = new Size(130, 35), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite };
            btnStartLoop.FlatAppearance.BorderSize = 0;

            btnStopLoop = new Button { Text = "🔲 停止测试", Location = new Point(165, 30), Size = new Size(130, 35), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopLoop.FlatAppearance.BorderColor = colorBorder;

            Button btnClearLoop = new Button { Text = "🧹 清空日志", Location = new Point(310, 30), Size = new Size(130, 35), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnClearLoop.FlatAppearance.BorderColor = colorBorder;

            groupCtrl.Controls.AddRange(new Control[] { btnStartLoop, btnStopLoop, btnClearLoop });

            logLoop = new TextBox
            {
                Location = new Point(15, 115),
                Size = new Size(880, 460),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = colorConsoleBg,
                ForeColor = Color.White,
                Font = new Font("Consolas", 10F, FontStyle.Regular)
            };
            tabLoop.Controls.Add(logLoop);
            AutoStretch(tabLoop, logLoop, width: true, height: true, rightMargin: 15, bottomMargin: 15);

            btnStartLoop.Click += async (s, e) => { await RunLoopbackTestAsync(); };
            btnStopLoop.Click += (s, e) => { isLoopTesting = false; ctsLoop?.Cancel(); };
            btnClearLoop.Click += (s, e) => logLoop.Clear();
        }

        private async Task RunLoopbackTestAsync()
        {
            string localIp = GetActiveLocalIp();
            if (string.IsNullOrEmpty(localIp) || localIp.Equals("未连接网络"))
            {
                MessageBox.Show("未连接网络，无法投递特征测试报文！", "错误"); return;
            }

            btnStartLoop.Enabled = false; btnStopLoop.Enabled = true;
            logLoop.Clear(); loopReceiveCount = 0; isLoopTesting = true;

            logLoop.AppendText($"[{DateTime.Now:HH:mm:ss}] 🚀 正在初始化局域网环路测试线阻模块...\r\n");

            using (UdpClient udpClient = new UdpClient())
            {
                try
                {
                    // 彻底补齐并实现原来损坏且截断的物理层套接字高级重组架构
                    udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, LoopTestPort));
                    udpClient.EnableBroadcast = true;
                    ctsLoop = new CancellationTokenSource();

                    var listenTask = Task.Run(async () =>
                    {
                        while (isLoopTesting && !ctsLoop.Token.IsCancellationRequested)
                        {
                            try
                            {
                                var result = await udpClient.ReceiveAsync();
                                string msg = Encoding.UTF8.GetString(result.Buffer);
                                if (msg == LoopToken)
                                {
                                    Interlocked.Increment(ref loopReceiveCount);
                                    this.Invoke(new Action(() => {
                                        logLoop.AppendText($"[{DateTime.Now:HH:mm:ss}] 🚨 【环路警告】检测到局域网高威回传风暴！来自：{result.RemoteEndPoint.Address}\r\n");
                                    }));
                                }
                            }
                            catch { break; }
                        }
                    });

                    logLoop.AppendText($"[{DateTime.Now:HH:mm:ss}] 🛰️ 正在全网投递环路探针广播特征令牌序列...\r\n");
                    byte[] tokenBytes = Encoding.UTF8.GetBytes(LoopToken);
                    IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, LoopTestPort);

                    for (int i = 0; i < 3; i++)
                    {
                        if (!isLoopTesting) break;
                        await udpClient.SendAsync(tokenBytes, tokenBytes.Length, broadcastEP);
                        await Task.Delay(500);
                    }

                    logLoop.AppendText($"[{DateTime.Now:HH:mm:ss}] ⏳ 投递工作链完成，正在监听视窗内捕捉物理环流，请稍候...\r\n");
                    await Task.Delay(3000);
                }
                catch (Exception ex)
                {
                    logLoop.AppendText($"❌ 环路物理测试失败: {ex.Message}\r\n");
                }
                finally
                {
                    isLoopTesting = false;
                    udpClient.Close();
                    this.Invoke(new Action(() => {
                        btnStartLoop.Enabled = true;
                        btnStopLoop.Enabled = false;
                        logLoop.AppendText($"[{DateTime.Now:HH:mm:ss}] 🏁 环路审计结束。累计拦截风暴自身回包：{loopReceiveCount} 次。\r\n");
                    }));
                }
            }
        }
        #endregion
    }
}
