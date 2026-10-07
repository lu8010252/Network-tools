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
        #region ====== 11.5 通用辅助函数 (ARP/导出/PowerShell/硬件信息等) ======
        private string GetActiveLocalIp()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name == currentIfaceName)
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            return addr.Address.ToString();
                    }
                }
            }
            return "未连接网络";
        }

        private string GetMacViaArp(string ipStr)
        {
            try
            {
                if (IPAddress.TryParse(ipStr, out IPAddress ip))
                {
                    uint destIp = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
                    byte[] macBuf = new byte[6];
                    uint macLen = (uint)macBuf.Length;
                    if (SendARP(destIp, 0, macBuf, ref macLen) == 0)
                        return BitConverter.ToString(macBuf);
                }
            }
            catch { }
            return "—";
        }

        private byte[] GetMacAddressByIp(string ipStr)
        {
            try
            {
                if (IPAddress.TryParse(ipStr, out IPAddress ip))
                {
                    uint destIp = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
                    byte[] macBuf = new byte[6];
                    uint macLen = (uint)macBuf.Length;
                    if (SendARP(destIp, 0, macBuf, ref macLen) == 0)
                        return macBuf;
                }
            }
            catch { }
            return null;
        }

        private string GetVendorName(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac == "—") return "未知厂商";
            string prefix = mac.Replace("-", "").Substring(0, 6).ToUpper();
            if (prefix.StartsWith("000C29") || prefix.StartsWith("005056")) return "VMware Inc.";
            if (prefix.StartsWith("00155D")) return "Microsoft Corp.";
            if (prefix.StartsWith("B0F893") || prefix.StartsWith("3C46D8")) return "Xiaomi Communications";
            if (prefix.StartsWith("00E04C")) return "Realtek Semiconductor";
            if (prefix.StartsWith("8C1645") || prefix.StartsWith("DC5360")) return "Huawei Technologies";
            if (prefix.StartsWith("04D9F5")) return "ASUSTek Computer";
            if (prefix.StartsWith("A4BB6D")) return "Intel Corporation";
            return "局域网通用设备终端";
        }

        private void SaveConsoleLog(string text, string title)
        {
            using (var sfd = new SaveFileDialog { Title = title, Filter = "文本文件|*.txt", FileName = $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.txt" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(sfd.FileName, text, Encoding.UTF8);
                    MessageBox.Show("日志数据正常导出成功！", "提示");
                }
            }
        }

        private void ExportListViewToCsv(ListView lv, string title)
        {
            using (var sfd = new SaveFileDialog { Title = title, Filter = "CSV文件|*.csv", FileName = $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    StringBuilder sb = new StringBuilder();
                    List<string> cols = new List<string>();
                    foreach (ColumnHeader ch in lv.Columns) cols.Add($"\"{ch.Text}\"");
                    sb.AppendLine(string.Join(",", cols));

                    foreach (ListViewItem item in lv.Items)
                    {
                        List<string> cells = new List<string>();
                        for (int i = 0; i < item.SubItems.Count; i++) cells.Add($"\"{item.SubItems[i].Text}\"");
                        sb.AppendLine(string.Join(",", cells));
                    }
                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("CSV数据报表导出成功！", "提示");
                }
            }
        }

        /// <summary>
        /// 【修复】上一版这里 stdout 固定用 GB2312 解码、stderr 却没指定编码（用了默认值），
        /// 两路编码不一致，一旦 stderr 里有中文照样会乱码；而且 GB2312 这个代码页在 .NET Core/5+
        /// 上同样需要注册 System.Text.Encoding.CodePages 才能用，没注册的话这里会直接抛异常。
        /// 改成读原始字节、按 BOM/字节特征自动判断是 UTF-16 还是 GBK，stdout/stderr 统一处理。
        /// </summary>
        private string ReadRawAndDecode(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                byte[] data = ms.ToArray();
                if (data.Length == 0) return "";

                if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) // UTF-16LE BOM
                    return Encoding.Unicode.GetString(data, 2, data.Length - 2);
                if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) // UTF-8 BOM
                    return Encoding.UTF8.GetString(data, 3, data.Length - 3);

                int sampleLen = Math.Min(data.Length, 40);
                int zeroAtOdd = 0;
                for (int i = 1; i < sampleLen; i += 2) if (data[i] == 0x00) zeroAtOdd++;
                if (sampleLen >= 4 && zeroAtOdd >= sampleLen / 4)
                    return Encoding.Unicode.GetString(data);

                try
                {
                    var enc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    return enc.GetString(data);
                }
                catch (NotSupportedException) { return Encoding.UTF8.GetString(data); }
            }
        }

        /// <summary>
        /// 【修复】之前直接拼 "-Command \"...\"" 这种写法，一旦 PowerShell 脚本内部也用了双引号
        /// （比如做字符串插值 "$($_.Name)"），双引号嵌套双引号，在命令行这一层解析时会被
        /// 提前截断，PowerShell 收到的是被切碎的命令，结果就是乱码/命令碎片。
        /// 改用 -EncodedCommand（脚本转成 Base64 传过去），彻底绕开引号转义这个坑，
        /// 以后所有调用 PowerShell 脚本的地方都应该走这个方法，不要再手动拼 -Command 字符串。
        /// </summary>
        private string RunPowerShellScript(string script)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(script);
            string encoded = Convert.ToBase64String(bytes);
            return RunCmd("powershell", $"-NoProfile -NonInteractive -EncodedCommand {encoded}");
        }

        /// <summary>
        /// 【新增】识别当前 WiFi 连接是 2.4G 还是 5G、以及大概是 WiFi 几代，
        /// 用的是系统自带的 netsh wlan show interfaces，解析"频道"(channel)和
        /// "无线电类型"(radio type)两行；中英文系统的标签文字不一样，两种都兼容一下。
        /// 解析不到就干脆不显示这行，不影响其它信息正常展示。
        /// </summary>
        private string GetWifiBandInfo()
        {
            try
            {
                string output = RunCmd("netsh", "wlan show interfaces");
                if (string.IsNullOrWhiteSpace(output)) return "";

                // 【修复】之前死抠"信道"/"无线电类型"这几个具体字段名，但这几个字段在不同
                // Windows 版本/语言包下的实际文案不完全一样（还见过"射频类型""频道"等变体），
                // 死等固定文案很容易全军覆没。这次换个更稳的思路：
                // - 频道号：只要一行的"字段名"里含有"道"这个字（信道/频道/射频信道 都含这个字），
                //   或者英文字段名是 Channel，就认为是它，不再要求精确匹配某几个固定词。
                // - 无线制式(802.11xx)：直接在整段输出里用正则找"802.11"开头的那串字符，
                //   不管它挂在哪个字段名后面，只要出现了就能识别，比对字段名更保险。
                int channel = -1;
                foreach (var rawLine in output.Split('\n'))
                {
                    string line = rawLine.Trim();
                    int idx = line.IndexOf(':');
                    if (idx < 0) continue;
                    string key = line.Substring(0, idx).Trim();
                    string val = line.Substring(idx + 1).Trim();

                    if (key.Contains("道") || key.Equals("Channel", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, out int ch)) { channel = ch; break; }
                    }
                }

                string radioType = "";
                var m = Regex.Match(output, @"802\.11[a-zA-Z/]*");
                if (m.Success) radioType = m.Value;

                if (channel <= 0 && string.IsNullOrEmpty(radioType)) return "";

                string band = channel <= 0 ? "" : (channel <= 14 ? "2.4G" : "5G");
                string wifiGen = "";
                string rt = radioType.ToLowerInvariant();
                if (rt.Contains("ax")) wifiGen = "WiFi 6";
                else if (rt.Contains("ac")) wifiGen = "WiFi 5";
                else if (rt.Contains("n")) wifiGen = "WiFi 4";
                else if (rt.Contains("g") || rt.Contains("b") || rt.Contains("a")) wifiGen = "WiFi 3及以下";

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(band)) parts.Add(band);
                if (!string.IsNullOrEmpty(wifiGen)) parts.Add(wifiGen + (string.IsNullOrEmpty(radioType) ? "" : $"({radioType})"));
                if (parts.Count == 0) return "";
                return "WiFi 频段: " + string.Join(" · ", parts);
            }
            catch { return ""; }
        }

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        /// <summary>
        /// 【新增】读取本机硬件配置（CPU / 内存 / 硬盘 / 显卡），按你的要求"显示正常就行"，
        /// 所以没有引入 WMI(System.Management) 这类需要额外程序集引用的东西，
        /// 全部走注册表读取 + Win32 API + .NET 自带的 DriveInfo，兼容性更好，不容易因为
        /// 项目没引用某个程序集而编译失败。显卡/CPU如果注册表路径在个别机型上没有，
        /// 就显示"未知"，不会让整个看板报错。
        /// </summary>
        private async Task RefreshHardwareInfoAsync()
        {
            await Task.Run(() => {
                // CPU 名称：注册表 HARDWARE\DESCRIPTION\System\CentralProcessor\0
                string cpuName = "未知";
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    {
                        var val = key?.GetValue("ProcessorNameString") as string;
                        if (!string.IsNullOrWhiteSpace(val)) cpuName = val.Trim();
                    }
                }
                catch { }

                // 显卡名称：注册表显示适配器类的第一个子项(0000) DriverDesc
                string gpuName = "未知";
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"))
                    {
                        var val = key?.GetValue("DriverDesc") as string;
                        if (!string.IsNullOrWhiteSpace(val)) gpuName = val.Trim();
                    }
                }
                catch { }

                // 内存：GlobalMemoryStatusEx，取总容量(GB)
                string ramText = "未知";
                try
                {
                    var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                    if (GlobalMemoryStatusEx(ref ms))
                    {
                        double totalGb = ms.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
                        ramText = $"{totalGb:0.#} GB (占用 {ms.dwMemoryLoad}%)";
                    }
                }
                catch { }

                // 硬盘：系统盘(C:)总容量/可用空间
                string diskText = "未知";
                try
                {
                    var drive = new DriveInfo("C");
                    if (drive.IsReady)
                    {
                        double totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                        double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                        diskText = $"{totalGb:0} GB (可用 {freeGb:0} GB)";
                    }
                }
                catch { }

                // 【补充防御】不管调用时机有没有踩准，这里再加一道保险：确认句柄已创建才 Invoke，
                // 万一还是有某个时机没考虑到，也不会再是"异常静默消失、标签永远卡住"，
                // 而是直接跳过这次更新，下次刷新还有机会正常显示。
                try
                {
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.Invoke(new Action(() => {
                            lblHwCpu.Text = $"CPU: {cpuName}";
                            lblHwRam.Text = $"内存: {ramText}";
                            lblHwDisk.Text = $"硬盘(C:): {diskText}";
                            lblHwGpu.Text = $"显卡: {gpuName}";
                        }));
                    }
                }
                catch { }
            });
        }

        private string RunCmd(string filename, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                    // 注意：不再固定设置 StandardOutputEncoding，交给 ReadRawAndDecode 自动判断
                };
                using (Process p = Process.Start(psi))
                {
                    string output = ReadRawAndDecode(p.StandardOutput.BaseStream);
                    string error = ReadRawAndDecode(p.StandardError.BaseStream);
                    p.WaitForExit();
                    return output + "\r\n" + error;
                }
            }
            catch (Exception ex)
            {
                return "内核指令调用发生异常: " + ex.Message;
            }
        }
        #endregion
    }
}
