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
        #region ====== 5. 网络状态看板与快捷切换 ======
        private void BuildTabNetLayout()
        {
            // 【调整】按你的要求，把网络看板改成固定宽度（不再撑满整行），右边腾出空间
            // 放一个新的"本机硬件配置"面板；看板内部右栏的状态几项也往左挪了一些。
            panelDash = new Panel { Location = new Point(15, 15), Size = new Size(560, 195), BorderStyle = BorderStyle.FixedSingle, BackColor = colorPrimaryLt };
            tabNet.Controls.Add(panelDash);

            lblDashTitle = new Label { Text = "当前系统网络连通性健康看板", Location = new Point(14, 12), Size = new Size(320, 20), Font = fontTitle, ForeColor = colorPrimary };
            lblIface = new Label { Text = "操作网卡: 检测中...", Location = new Point(18, 44), Size = new Size(240, 20), Font = fontNormal, ForeColor = textDark };
            lblIP = new Label { Text = "当前 IP: 检测中...", Location = new Point(18, 70), Size = new Size(240, 20), Font = fontBold };
            // 【新增】按你的要求加的几项常用信息：子网掩码、MAC地址、DHCP/静态获取方式、链路速率。
            // 这四项统一用跟"操作网卡"一样的常规字体/颜色（偏静态的事实类信息），
            // "获取方式"用粗体+颜色区分 DHCP/静态，风格上对齐"网关/DNS/互联网"这几个状态项。
            lblSubnet = new Label { Text = "子网掩码: 检测中...", Location = new Point(18, 96), Size = new Size(240, 20), Font = fontNormal, ForeColor = textDark };
            lblMac = new Label { Text = "MAC 地址: 检测中...", Location = new Point(18, 122), Size = new Size(240, 20), Font = fontNormal, ForeColor = textDark };
            lblIpMode = new Label { Text = "获取方式: 检测中...", Location = new Point(18, 148), Size = new Size(240, 20), Font = fontNormal, ForeColor = textDark };

            lblStatusGw = new Label { Text = "局域网网关: 正在检测", AutoSize = true, Location = new Point(270, 44), Font = fontBold };
            lblStatusDns = new Label { Text = "DNS 服务: 正在检测", AutoSize = true, Location = new Point(270, 70), Font = fontBold };
            lblStatusWan = new Label { Text = "互联网连通: 正在检测", AutoSize = true, Location = new Point(270, 96), Font = fontBold };
            lblLinkSpeed = new Label { Text = "链路速率: 检测中...", AutoSize = true, Location = new Point(270, 122), Font = fontNormal, ForeColor = textDark };
            lblWifiBand = new Label { Text = "", AutoSize = true, Location = new Point(270, 148), Font = fontNormal, ForeColor = textDark };
            panelDash.Controls.AddRange(new Control[] { lblDashTitle, lblIface, lblIP, lblSubnet, lblMac, lblIpMode, lblStatusGw, lblStatusDns, lblStatusWan, lblLinkSpeed, lblWifiBand });
            Stylize(panelDash, 14);

            // 【新增】电脑硬件配置面板：CPU / 内存 / 硬盘 / 显卡，读的是本机真实硬件信息，
            // 不需要额外的第三方库或 WMI 引用，走注册表 + Win32 API 读取，兼容性更好。
            Panel panelHw = new Panel { Location = new Point(590, 15), Size = new Size(305, 195), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(247, 245, 255) };
            tabNet.Controls.Add(panelHw);
            AutoStretch(tabNet, panelHw);

            Label lblHwTitle = new Label { Text = "本机硬件配置", Location = new Point(14, 12), Font = fontTitle, ForeColor = colorPrimary, AutoSize = true };
            lblHwCpu = new Label { Text = "CPU: 检测中...", Location = new Point(18, 44), Size = new Size(270, 40), Font = fontNormal, ForeColor = textDark };
            lblHwRam = new Label { Text = "内存: 检测中...", Location = new Point(18, 88), Size = new Size(270, 20), Font = fontNormal, ForeColor = textDark };
            lblHwDisk = new Label { Text = "硬盘(C:): 检测中...", Location = new Point(18, 112), Size = new Size(270, 20), Font = fontNormal, ForeColor = textDark };
            lblHwGpu = new Label { Text = "显卡: 检测中...", Location = new Point(18, 136), Size = new Size(270, 40), Font = fontNormal, ForeColor = textDark };
            panelHw.Controls.AddRange(new Control[] { lblHwTitle, lblHwCpu, lblHwRam, lblHwDisk, lblHwGpu });
            Stylize(panelHw, 14);
            // 【修复】这里原来直接调用 RefreshHardwareInfoAsync()，但这时候正处于构造函数阶段，
            // 窗体句柄可能还没创建好，里面的 this.Invoke 会直接抛异常，异常又没人接住、静默消失，
            // 标签就一直卡在"检测中..."。改成挪到 OnLoad 里、确保句柄已创建之后再调用（见下方）。

            Button btnHome = CreateStyledButton("常用 1 段 IP 地址", 15, 223, 180, 42);
            btnHome.Click += async (s, e) => {
                await ExecuteNetshAsync(
                $"interface ip set address name=\"{currentIfaceName}\" static {homeIp} {homeMask} {homeGw} 1",
                $"interface ip set dns name=\"{currentIfaceName}\" static {homeDns1}",
                $"interface ip add dns name=\"{currentIfaceName}\" {homeDns2} index=2");
            };

            Button btnWork = CreateStyledButton("常用 0 段 IP 地址", 210, 223, 180, 42);
            btnWork.Click += async (s, e) => {
                await ExecuteNetshAsync(
                $"interface ip set address name=\"{currentIfaceName}\" static {workIp} {workMask} {workGw} 1",
                $"interface ip set dns name=\"{currentIfaceName}\" static {workDns1}",
                $"interface ip add dns name=\"{currentIfaceName}\" {workDns2} index=2");
            };

            Button btnManual = CreateStyledButton("自定义手动输入", 405, 223, 180, 42);
            btnManual.Click += (s, e) => { OpenCustomInputForm(); };

            Button btnDhcp = CreateStyledButton("自动获取 DHCP", 600, 223, 180, 42);
            btnDhcp.Click += async (s, e) => {
                await ExecuteNetshAsync(
                $"interface ip set address name=\"{currentIfaceName}\" dhcp",
                $"interface ip set dns name=\"{currentIfaceName}\" dhcp");
            };

            tabNet.Controls.AddRange(new Control[] { btnHome, btnWork, btnManual, btnDhcp });

            GroupBox netGroup = new GroupBox { Text = " 高级网络运维扩展 ", Location = new Point(15, 290), Size = new Size(880, 340), Font = fontBold, ForeColor = colorPrimary };
            tabNet.Controls.Add(netGroup);
            AutoStretch(tabNet, netGroup, width: true, height: true, rightMargin: 15, bottomMargin: 15);

            Button btnSpeed = CreateStyledButton("手动刷新状态 / 检测延迟", 20, 40, 230, 42, false);
            btnSpeed.Click += async (s, e) => await RefreshNetStatusAsync();

            Button btnRepair = CreateStyledButton("一键网络深度重置", 20, 100, 230, 42, false);
            btnRepair.Click += async (s, e) => {
                lblIP.Text = "网络底层重置中...";
                await Task.Run(() => {
                    RunCmd("ipconfig", "/flushdns");
                    RunCmd("ipconfig", $"/release \"{currentIfaceName}\"");
                    Task.Delay(1000).Wait();
                    RunCmd("ipconfig", $"/renew \"{currentIfaceName}\"");
                });
                await RefreshNetStatusAsync();
            };

            Button btnReport = CreateStyledButton("一键生成诊断报告", 20, 160, 230, 42, false);
            btnReport.Click += async (s, e) => await ExportDiagnosticReportAsync();

            // 【新增】改配置前会自动备份，这个按钮用来把备份恢复回去
            Button btnRestore = CreateStyledButton("恢复上一次配置", 20, 220, 230, 42, false);
            btnRestore.Click += async (s, e) => await RestoreLastIpConfigAsync();

            // 【精简】"查看断网/异常历史日志" 按钮按你的要求去掉了；
            // "查看IP修改执行日志" 挪到"系统高级工具"页去了，首页只留最常用的四个操作。
            netGroup.Controls.AddRange(new Control[] { btnSpeed, btnRepair, btnReport, btnRestore });

            Label lblTip = new Label
            {
                Text = "运维小常识：\n\n1. 如果切换网络后看板显示 [阻塞]，说明网关不可达，请检查物理线路。\n2. 如果网关正常但互联网 [断开]，通常是上行宽带欠费或外部光猫断开。\n3. 一键深度重置会清理系统本地 DNS 静态缓存。\n4. 准确的主机发现依赖链路层物理状态，本工具集成高精ARP交叉穿透审计。\n5. 探测局域网流氓DHCP服务请前往专门的“DHCP服务器检测”页面。",
                Location = new Point(280, 42),
                Size = new Size(580, 300),
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei", 10F)
            };
            netGroup.Controls.Add(lblTip);
            AutoStretch(netGroup, lblTip, width: true, height: true, rightMargin: 15, bottomMargin: 15);
        }

        private async Task RefreshNetStatusAsync()
        {
            lblIP.Text = "正在扫描网络..."; lblIP.ForeColor = Color.Orange;
            await Task.Run(() => {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 核心过滤：剔除所有虚拟网卡和回环设备，确保锁定真正的物理有线/无线网卡
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        !ni.Description.Contains("VMware") &&
                        !ni.Description.Contains("Virtual") &&
                        !ni.Description.Contains("vEthernet") &&
                        !ni.Description.Contains("VirtualBox"))
                    {
                        currentIfaceName = ni.Name; break;
                    }
                }
            });
            lblIface.Text = $"操作网卡: {currentIfaceName}";
            string localIp = "未连接网络"; string gatewayIp = null; bool isConnected = false;
            string subnetMask = "—"; string macAddress = "—"; string ipModeText = "—";
            string linkSpeedText = "—"; string wifiBandText = "";
            var dnsList = new List<string>();

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name == currentIfaceName)
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            localIp = addr.Address.ToString(); isConnected = true;
                            // 【新增】子网掩码，跟当前 IP 是同一条 UnicastAddress 记录里带出来的
                            if (addr.IPv4Mask != null) subnetMask = addr.IPv4Mask.ToString();
                        }
                    }

                    // 核心优化：显式进行网络族检测，过滤掉 IPv6 格式，只拉取 IPv4 的真实物理网关
                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            gatewayIp = gw.Address.ToString();
                            break;
                        }
                    }

                    foreach (var d in ipProps.DnsAddresses)
                    {
                        if (d.AddressFamily == AddressFamily.InterNetwork) dnsList.Add(d.ToString());
                    }

                    // 【新增】MAC 地址，格式化成常见的 XX-XX-XX-XX-XX-XX 形式
                    try
                    {
                        byte[] macBytes = ni.GetPhysicalAddress().GetAddressBytes();
                        if (macBytes.Length > 0) macAddress = string.Join("-", macBytes.Select(b => b.ToString("X2")));
                    }
                    catch { }

                    // 【新增】DHCP 自动获取 还是 静态手动配置，来自 IPv4Properties.IsDhcpEnabled
                    try
                    {
                        var v4props = ipProps.GetIPv4Properties();
                        if (v4props != null)
                        {
                            ipModeText = v4props.IsDhcpEnabled ? "获取方式: [DHCP 自动获取]" : "获取方式: [静态手动配置]";
                        }
                    }
                    catch { }

                    // 【新增】链路速率(Mbps)，顺手标出是有线还是无线网卡
                    try
                    {
                        string mediaType = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "无线" :
                                            ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "有线" : ni.NetworkInterfaceType.ToString();
                        if (ni.Speed > 0)
                            linkSpeedText = $"链路速率: {ni.Speed / 1000000} Mbps ({mediaType})";
                        else
                            linkSpeedText = $"链路速率: 未知 ({mediaType})";
                    }
                    catch { }

                    // 【新增】如果是无线网卡，顺手识别一下当前连的是 2.4G 还是 5G、以及 WiFi 几，
                    // 走的是 netsh wlan show interfaces 这个系统自带命令，解析里面的"频道"和
                    // "无线电类型"两行；有线网卡这里不显示。
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        wifiBandText = await Task.Run(() => GetWifiBandInfo());
                    }
                }
            }
            if (isConnected) { lblIP.Text = $"当前 IP: {localIp}"; lblIP.ForeColor = colorAccentG; }
            else { lblIP.Text = "当前 IP: 未连接网络"; lblIP.ForeColor = colorAccentR; }

            lblSubnet.Text = $"子网掩码: {subnetMask}";
            lblMac.Text = $"MAC 地址: {macAddress}";
            lblIpMode.Text = ipModeText;
            lblLinkSpeed.Text = linkSpeedText;
            lblWifiBand.Text = wifiBandText;

            string dnsProbe = dnsList.Count > 0 ? dnsList[0] : "223.5.5.5";
            string dnsDisplay = dnsList.Count > 0 ? string.Join(", ", dnsList) : null;

            _ = Task.Run(() => CheckPingStatus(gatewayIp, lblStatusGw, "局域网网关", false, gatewayIp));
            _ = Task.Run(() => CheckPingStatus(dnsProbe, lblStatusDns, "DNS 服务", true, dnsDisplay));
            _ = CheckWanStatusAsync();
        }

        /// <summary>
        /// 【新增】互联网连通性检测单独拎出来一个方法：Ping 通了之后，再顺手查一次公网出口IP显示出来。
        /// 查公网IP用的是第三方在线接口(api.ipify.org)，查不到（比如被墙、超时）不影响连通性判断本身，
        /// 静默失败即可，不会因为这个额外请求让整个健康看板报错或卡住。
        /// </summary>
        private async Task CheckWanStatusAsync()
        {
            bool connected = false; long rtt = 0;
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply reply = await p.SendPingAsync("www.baidu.com", 800);
                    connected = reply.Status == IPStatus.Success;
                    rtt = reply.RoundtripTime;
                }
            }
            catch { connected = false; }

            if (!connected)
            {
                UpdateLabel(lblStatusWan, "互联网连通: [断开]", colorAccentR);
                return;
            }

            // 【修复】原来查公网IP用的 api.ipify.org 是国外接口，国内网络不挂代理基本连不上，
            // 导致公网IP一直显示不出来。改成优先用国内可直连的 3322.org，连不上再退回国外接口兜底
            // （方便挂了代理的用户依然能用），两个都失败就干脆不显示这部分，不影响其它状态判断。
            string publicIp = await FetchPublicIpAsync();

            string suffix = string.IsNullOrEmpty(publicIp) ? "" : $"  公网IP: {publicIp}";
            if (rtt >= 150)
                UpdateLabel(lblStatusWan, "互联网连通: [畅通但延迟高]" + suffix + $" {rtt}ms", colorWarn);
            else
                UpdateLabel(lblStatusWan, "互联网连通: [畅通]" + suffix, colorAccentG);
        }

        private static readonly string[] publicIpEndpoints =
        {
            "http://members.3322.org/dyndns/getip", // 国内可直连，不需要梯子
            "https://api.ipify.org"                  // 国外备用，本机能访问外网时才有效
        };

        private async Task<string> FetchPublicIpAsync()
        {
            foreach (var url in publicIpEndpoints)
            {
                try
                {
                    string result = (await publicIpHttpClient.GetStringAsync(url)).Trim();
                    if (IPAddress.TryParse(result, out _)) return result; // 校验一下确实是个IP，避免网页错误内容被当成IP显示
                }
                catch { /* 这个接口不通就换下一个 */ }
            }
            return null;
        }

        private void CheckPingStatus(string target, Label label, string prefix, bool isDns = false, string displayValue = null)
        {
            string suffix = string.IsNullOrEmpty(displayValue) ? "" : $"  ({displayValue})";
            if (string.IsNullOrEmpty(target)) { UpdateLabel(label, $"{prefix}: [无设备]" + suffix, colorAccentR); return; }
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply reply = p.Send(target, 800);
                    if (reply.Status == IPStatus.Success)
                    {
                        if (reply.RoundtripTime >= 150)
                            UpdateLabel(label, $"{prefix}: " + (isDns ? "[畅通但延迟高]" : "[延迟偏高]") + suffix + $" {reply.RoundtripTime}ms", colorWarn);
                        else
                            UpdateLabel(label, $"{prefix}: " + (isDns ? "[畅通]" : "[正常]") + suffix, colorAccentG);
                    }
                    else
                    {
                        UpdateLabel(label, $"{prefix}: [阻塞]" + suffix, colorAccentR);
                    }
                }
            }
            catch
            {
                UpdateLabel(label, $"{prefix}: [异常]" + suffix, colorAccentR);
            }
        }

        private async Task ExportDiagnosticReportAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== 网络工具箱 · 诊断报告 ==========");
            sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("【健康看板快照】");
            sb.AppendLine(lblIface.Text);
            sb.AppendLine(lblIP.Text);
            sb.AppendLine(lblStatusGw.Text);
            sb.AppendLine(lblStatusDns.Text);
            sb.AppendLine(lblStatusWan.Text);
            sb.AppendLine();
            sb.AppendLine("【ipconfig /all 完整输出】");
            string ipAll = await Task.Run(() => RunCmd("ipconfig", "/all"));
            sb.AppendLine(ipAll);

            using (var sfd = new SaveFileDialog { Title = "导出诊断报告", Filter = "文本文件|*.txt", FileName = $"网络诊断报告_{DateTime.Now:yyyyMMdd_HHmmss}.txt" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    await Task.Run(() => File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8));
                    var result = MessageBox.Show("诊断报告已导出，是否立即打开查看？", "导出成功", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (result == DialogResult.Yes) Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
            }
        }

        private void UpdateLabel(Label lbl, string text, Color color)
        {
            if (lbl.InvokeRequired) lbl.Invoke(new Action(() => { lbl.Text = text; lbl.ForeColor = color; }));
            else { lbl.Text = text; lbl.ForeColor = color; }
        }

        private static readonly string netshLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "netsh_execute_log.txt");

        /// <summary>
        /// 【修复】主窗体的缩放逻辑写在 Form1 自己的 OnLoad 里，但每个弹窗都是独立的 Form 实例，
        /// 各自的 OnLoad 里没有这段逻辑，所以主界面缩放正常了、弹窗却还是原来的小尺寸。
        /// 这个方法给任意弹窗 Form 补上同样的"读当前显示器DPI、按比例强制Scale"逻辑，
        /// 每个新建的弹窗在 ShowDialog() 之前调用一下这个方法即可。
        /// </summary>
        private void ApplyDialogDpiScale(Form dialog)
        {
            dialog.Load += (s, e) =>
            {
                float sf = dialog.DeviceDpi / 96f;
                if (Math.Abs(sf - 1.0f) > 0.01f)
                {
                    dialog.Scale(new SizeF(sf, sf));
                    if (dialog.Owner != null)
                    {
                        dialog.Location = new Point(
                            dialog.Owner.Left + (dialog.Owner.Width - dialog.Width) / 2,
                            dialog.Owner.Top + (dialog.Owner.Height - dialog.Height) / 2);
                    }
                    else
                    {
                        var wa = Screen.FromControl(dialog).WorkingArea;
                        dialog.Location = new Point(wa.Left + (wa.Width - dialog.Width) / 2, wa.Top + (wa.Height - dialog.Height) / 2);
                    }
                }
            };
        }

        private bool IsValidIPv4(string s) => !string.IsNullOrWhiteSpace(s) && IPAddress.TryParse(s, out var addr) && addr.AddressFamily == AddressFamily.InterNetwork;

        /// <summary>把 "192.168.1.10" 这种IP字符串转成可以直接比大小的数值，用于按IP地址数值大小排序（不是字符串排序，避免"10"排在"2"前面这种问题）。</summary>
        private long IpTextToSortKey(string ip)
        {
            long val = 0;
            foreach (var part in ip.Split('.'))
            {
                val = val * 256 + (int.TryParse(part, out int n) ? n : 0);
            }
            return val;
        }

        private static readonly string ipConfigBackupPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ip_config_backup.txt");

        /// <summary>【新增功能】改配置之前，把当前网卡真实生效的 IP/掩码/网关/DNS 存一份备份文件，方便改坏了能一键恢复。</summary>
        private void BackupCurrentIpConfig()
        {
            try
            {
                string ip = "", mask = "", gw = "";
                var dnsList = new List<string>();
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.Name != currentIfaceName) continue;
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ip = addr.Address.ToString();
                            if (addr.IPv4Mask != null) mask = addr.IPv4Mask.ToString();
                        }
                    foreach (var gwAddr in ipProps.GatewayAddresses)
                        if (gwAddr.Address.AddressFamily == AddressFamily.InterNetwork) { gw = gwAddr.Address.ToString(); break; }
                    foreach (var d in ipProps.DnsAddresses)
                        if (d.AddressFamily == AddressFamily.InterNetwork) dnsList.Add(d.ToString());
                }
                if (string.IsNullOrEmpty(ip)) return; // 拿不到当前有效配置就不备份，避免用空值把之前真正有用的备份覆盖掉

                var sb = new StringBuilder();
                sb.AppendLine($"IFACE={currentIfaceName}");
                sb.AppendLine($"IP={ip}");
                sb.AppendLine($"MASK={mask}");
                sb.AppendLine($"GW={gw}");
                sb.AppendLine($"DNS1={(dnsList.Count > 0 ? dnsList[0] : "")}");
                sb.AppendLine($"DNS2={(dnsList.Count > 1 ? dnsList[1] : "")}");
                sb.AppendLine($"TIME={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                File.WriteAllText(ipConfigBackupPath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* 备份失败不阻塞主流程，静默跳过 */ }
        }

        /// <summary>【新增功能】读取上一次的备份文件，恢复成那时候的 IP/掩码/网关/DNS 配置。</summary>
        private async Task RestoreLastIpConfigAsync()
        {
            if (!File.Exists(ipConfigBackupPath)) { MessageBox.Show("暂无备份记录，还没有改过IP配置。", "提示"); return; }

            var dict = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(ipConfigBackupPath))
            {
                int idx = line.IndexOf('=');
                if (idx > 0) dict[line.Substring(0, idx)] = line.Substring(idx + 1);
            }
            if (!dict.TryGetValue("IP", out string ip) || string.IsNullOrEmpty(ip)) { MessageBox.Show("备份记录无效。", "提示"); return; }

            string mask = dict.TryGetValue("MASK", out var m) ? m : "255.255.255.0";
            string gw = dict.TryGetValue("GW", out var g) ? g : "";
            string dns1 = dict.TryGetValue("DNS1", out var d1) ? d1 : "";
            string dns2 = dict.TryGetValue("DNS2", out var d2) ? d2 : "";
            string time = dict.TryGetValue("TIME", out var t) ? t : "未知时间";

            var confirm = MessageBox.Show($"将恢复到 {time} 保存的配置：\nIP: {ip}\n掩码: {mask}\n网关: {gw}\nDNS: {dns1} {dns2}\n\n确定要恢复吗？", "确认恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            var cmds = new List<string>();
            cmds.Add(!string.IsNullOrEmpty(gw)
                ? $"interface ip set address name=\"{currentIfaceName}\" static {ip} {mask} {gw} 1"
                : $"interface ip set address name=\"{currentIfaceName}\" static {ip} {mask}");
            if (!string.IsNullOrEmpty(dns1))
            {
                cmds.Add($"interface ip set dns name=\"{currentIfaceName}\" static {dns1}");
                if (!string.IsNullOrEmpty(dns2)) cmds.Add($"interface ip add dns name=\"{currentIfaceName}\" {dns2} index=2");
            }
            await ExecuteNetshAsync(cmds.ToArray());
        }

        private async Task ExecuteNetshAsync(params string[] commands)
        {
            lblIP.Text = "正在应用配置..."; lblIP.ForeColor = Color.Orange;
            // 【新增】每次真正改配置之前，先把当前生效的配置存一份备份，改坏了能用"恢复上一次配置"退回去
            BackupCurrentIpConfig();
            await Task.Run(() =>
            {
                foreach (var cmd in commands)
                {
                    if (string.IsNullOrWhiteSpace(cmd)) continue;
                    string result = RunCmd("netsh", cmd.Trim());
                    // 【诊断】不弹窗打扰你，但把每条命令的原始执行结果写进日志文件——
                    // netsh 正常成功执行是没有任何输出的，如果这里记到了内容，大概率就是报错信息
                    // （权限不足/参数不对/网卡名不匹配等）。万一 Win11 上还是没生效，把这个文件发我就能定位。
                    try
                    {
                        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] netsh {cmd.Trim()}\r\n" +
                                      $"返回: {(string.IsNullOrWhiteSpace(result) ? "(无输出 = 通常代表执行成功)" : result.Trim())}\r\n" +
                                      new string('-', 60) + "\r\n";
                        File.AppendAllText(netshLogPath, line, Encoding.UTF8);
                    }
                    catch { /* 日志写入失败不影响主流程，静默跳过 */ }
                }
            });
            await RefreshNetStatusAsync();
        }

        // 补齐并优雅实现的“自定义手动输入”窗体
        private void OpenCustomInputForm()
        {
            // 【修复+功能】默认值不再写死成 192.168.1.100 / 192.168.1.1，
            // 改成读取本机当前网卡的真实 IP / 掩码 / 网关。
            // 网关这里必须显式筛选 AddressFamily.InterNetwork(IPv4)——
            // 如果不筛选，遇到同时有 IPv6 链路本地网关的网卡，GatewayAddresses[0] 拿到的
            // 可能就是那个 "fe80::...%15" 的 IPv6 地址，传给 netsh 设置 IPv4 静态地址会导致
            // 参数不合法、点了没反应，这正是你截图里遇到的问题。
            string curIp = "192.168.1.100", curMask = "255.255.255.0", curGw = "192.168.1.1";
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name == currentIfaceName)
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            curIp = addr.Address.ToString();
                            if (addr.IPv4Mask != null) curMask = addr.IPv4Mask.ToString();
                        }
                    }
                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork) { curGw = gw.Address.ToString(); break; }
                    }
                }
            }

            Form f = new Form
            {
                Text = "自定义手动输入 IP 配置",
                Size = new Size(380, 320),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = colorWhite
            };

            Label lblIp = new Label { Text = "IP 地址:", Location = new Point(20, 25), AutoSize = true, Font = fontNormal };
            TextBox txtIp = new TextBox { Text = curIp, Location = new Point(120, 22), Size = new Size(200, 23), Font = fontNormal };

            Label lblMask = new Label { Text = "子网掩码:", Location = new Point(20, 65), AutoSize = true, Font = fontNormal };
            TextBox txtMask = new TextBox { Text = curMask, Location = new Point(120, 62), Size = new Size(200, 23), Font = fontNormal };

            Label lblGw = new Label { Text = "默认网关:", Location = new Point(20, 105), AutoSize = true, Font = fontNormal };
            TextBox txtGw = new TextBox { Text = curGw, Location = new Point(120, 102), Size = new Size(200, 23), Font = fontNormal };

            // 【新增】改IP地址的时候，默认网关自动跟着联动成同网段的 .1（比如IP改成
            // 192.168.20.212，网关自动变成192.168.20.1），省得网关是 .1 的情况下还要手动改一遍。
            // 如果你自己手动改过网关框的内容，就不会再被自动覆盖了。
            bool gwUserEdited = false;
            bool isAutoUpdatingGw = false;
            txtGw.TextChanged += (s, e) => { if (!isAutoUpdatingGw) gwUserEdited = true; };
            txtIp.TextChanged += (s, e) =>
            {
                if (gwUserEdited) return;
                var parts = txtIp.Text.Trim().Split('.');
                if (parts.Length == 4 && parts.Take(3).All(p => byte.TryParse(p, out _)))
                {
                    isAutoUpdatingGw = true;
                    txtGw.Text = $"{parts[0]}.{parts[1]}.{parts[2]}.1";
                    isAutoUpdatingGw = false;
                }
            };

            Label lblDns1 = new Label { Text = "首选 DNS:", Location = new Point(20, 145), AutoSize = true, Font = fontNormal };
            TextBox txtDns1 = new TextBox { Text = "223.5.5.5", Location = new Point(120, 142), Size = new Size(200, 23), Font = fontNormal };

            Label lblDns2 = new Label { Text = "备用 DNS:", Location = new Point(20, 185), AutoSize = true, Font = fontNormal };
            TextBox txtDns2 = new TextBox { Text = "114.114.114.114", Location = new Point(120, 182), Size = new Size(200, 23), Font = fontNormal };

            Button btnOk = new Button { Text = "应用配置", Location = new Point(60, 230), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite, Font = fontBold };
            btnOk.FlatAppearance.BorderSize = 0;

            Button btnCn = new Button { Text = "取消", Location = new Point(190, 230), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Font = fontBold };
            btnCn.FlatAppearance.BorderColor = colorBorder;

            btnCn.Click += (s, e) => f.Close();
            btnOk.Click += async (s, e) =>
            {
                string ip = txtIp.Text.Trim();
                string mask = txtMask.Text.Trim();
                string gw = txtGw.Text.Trim();
                string dns1 = txtDns1.Text.Trim();
                string dns2 = txtDns2.Text.Trim();

                // 【新增】应用之前先做一次格式校验，格式不对直接友好提示、不关闭弹窗，
                // 不用等传给 netsh 报一堆看不懂的英文错误才知道是自己填错了。
                if (!IsValidIPv4(ip)) { MessageBox.Show("IP 地址格式不对，请检查（例如 192.168.1.100）。", "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (!IsValidIPv4(mask)) { MessageBox.Show("子网掩码格式不对，请检查（例如 255.255.255.0）。", "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (!string.IsNullOrEmpty(gw) && !IsValidIPv4(gw)) { MessageBox.Show("默认网关格式不对，请检查。", "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (!string.IsNullOrEmpty(dns1) && !IsValidIPv4(dns1)) { MessageBox.Show("首选 DNS 格式不对，请检查。", "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (!string.IsNullOrEmpty(dns2) && !IsValidIPv4(dns2)) { MessageBox.Show("备用 DNS 格式不对，请检查。", "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

                f.Close();

                List<string> cmds = new List<string>();
                if (!string.IsNullOrEmpty(gw))
                    cmds.Add($"interface ip set address name=\"{currentIfaceName}\" static {ip} {mask} {gw} 1");
                else
                    cmds.Add($"interface ip set address name=\"{currentIfaceName}\" static {ip} {mask}");

                if (!string.IsNullOrEmpty(dns1))
                {
                    cmds.Add($"interface ip set dns name=\"{currentIfaceName}\" static {dns1}");
                    if (!string.IsNullOrEmpty(dns2))
                        cmds.Add($"interface ip add dns name=\"{currentIfaceName}\" {dns2} index=2");
                }
                await ExecuteNetshAsync(cmds.ToArray());
            };

            f.Controls.AddRange(new Control[] { lblIp, txtIp, lblMask, txtMask, lblGw, txtGw, lblDns1, txtDns1, lblDns2, txtDns2, btnOk, btnCn });
            ApplyDialogDpiScale(f); // 【修复】弹窗独立DPI缩放，之前只有主窗口缩放了、弹窗还是原来的小尺寸
            f.ShowDialog(this);
        }
        #endregion
    }
}
