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
        #region ====== 11.2 系统高级工具 ======
        private void BuildTabToolsLayout()
        {
            Label lbl = new Label { Text = "高级系统诊断工具台", Location = new Point(20, 20), AutoSize = true, Font = fontTitle, ForeColor = colorPrimary };
            tabTools.Controls.Add(lbl);

            // 【重构】原来这里有个"重启当前网络适配器"，跟状态看板首页的"一键网络深度重置"功能重叠，去掉了。
            // 改成快捷启动系统自带工具的网格（同类工具箱常见风格），保留几个真正有用、不重复的网络诊断小工具，
            // "查看IP修改执行日志" 也从首页挪过来了，放在这里更合适。
            var toolButtons = new (string label, Action action)[]
            {
                ("CMD控制台", () => LaunchTool("CMD控制台", "cmd.exe")),
                ("PowerShell", () => LaunchTool("PowerShell", "powershell.exe")),
                ("管理员CMD", () => LaunchToolAsAdmin("管理员CMD", "cmd.exe")),
                ("设备管理器", () => LaunchTool("设备管理器", "devmgmt.msc")),
                ("任务管理器", () => LaunchTool("任务管理器", "taskmgr.exe")),
                ("系统信息", () => LaunchTool("系统信息", "msinfo32.exe")),
                ("注册表编辑器", () => LaunchTool("注册表编辑器", "regedit.exe")),
                ("事件查看器", () => LaunchTool("事件查看器", "eventvwr.msc")),
                ("磁盘管理", () => LaunchTool("磁盘管理", "diskmgmt.msc")),
                ("服务管理", () => LaunchTool("服务管理", "services.msc")),
                ("资源监视器", () => LaunchTool("资源监视器", "resmon.exe")),
                ("计算机管理", () => LaunchTool("计算机管理", "compmgmt.msc")),
                ("控制面板", () => LaunchTool("控制面板", "control.exe")),
                ("远程桌面连接", () => LaunchTool("远程桌面连接", "mstsc.exe")),
                ("网络连接设置", () => LaunchTool("网络连接设置", "ncpa.cpl")),
                ("Windows防火墙", () => LaunchTool("Windows防火墙设置", "firewall.cpl")),
                ("清空DNS缓存", () => RunAndLogAsync("清空DNS缓存", "ipconfig", "/flushdns")),
                ("打开Hosts文件", OpenHostsFile),
                ("清空ARP缓存", () => RunAndLogAsync("清空ARP缓存", "netsh", "interface ip delete arpcache")),
                ("查看当前网络连接", () => RunAndLogAsync("查看网络连接(netstat)", "netstat", "-ano")),
                ("查看IP修改执行日志", OpenNetshLogFile),
                ("更改用户密码", OpenChangePasswordForm),
            };

            const int cols = 4, btnW = 200, btnH = 40, gapX = 8, gapY = 10, startX = 20, startY = 55;
            for (int idx = 0; idx < toolButtons.Length; idx++)
            {
                int row = idx / cols, col = idx % cols;
                var (label, action) = toolButtons[idx];
                Button b = CreateStyledButton(label, startX + col * (btnW + gapX), startY + row * (btnH + gapY), btnW, btnH, false);
                b.Click += (s, e) => action();
                tabTools.Controls.Add(b);
            }

            int totalRows = (int)Math.Ceiling(toolButtons.Length / (double)cols);
            int logY = startY + totalRows * (btnH + gapY) + 10;

            logTools = new TextBox { Location = new Point(20, logY), Size = new Size(860, 300), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = colorConsoleBg, ForeColor = Color.White, Font = new Font("Consolas", 10F) };
            logTools.Text = "该扩展高级模块就绪。点击上方按钮开始操作。";
            tabTools.Controls.Add(logTools);
            AutoStretch(tabTools, logTools, width: true, height: true, rightMargin: 15, bottomMargin: 15);
        }

        /// <summary>快捷启动一个系统自带工具（cmd/mmc控制台/cpl面板等），失败了记到日志里，不弹窗打扰。</summary>
        private void LaunchTool(string toolName, string fileName, string args = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(fileName, args ?? "") { UseShellExecute = true });
                AppendToolsLog($"已启动: {toolName}");
            }
            catch (Exception ex) { AppendToolsLog($"❌ 启动 {toolName} 失败: {ex.Message}"); }
        }

        private void LaunchToolAsAdmin(string toolName, string fileName, string args = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(fileName, args ?? "") { UseShellExecute = true, Verb = "runas" });
                AppendToolsLog($"已以管理员身份启动: {toolName}");
            }
            catch (Exception ex) { AppendToolsLog($"❌ 启动 {toolName} 失败(可能取消了UAC授权): {ex.Message}"); }
        }

        private void OpenHostsFile()
        {
            try
            {
                string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{hostsPath}\"") { UseShellExecute = true });
                AppendToolsLog($"已用记事本打开: {hostsPath}");
            }
            catch (Exception ex) { AppendToolsLog("❌ 打开失败: " + ex.Message); }
        }

        private async void RunAndLogAsync(string actionName, string exe, string args)
        {
            AppendToolsLog($"正在执行: {exe} {args} ...");
            string r = await Task.Run(() => RunCmd(exe, args));
            AppendToolsLog(string.IsNullOrWhiteSpace(r) ? $"✅ {actionName} 完成。" : r.Trim());
        }

        private void OpenNetshLogFile()
        {
            try
            {
                if (!File.Exists(netshLogPath)) { AppendToolsLog("暂无记录，还没有执行过 IP/DNS 修改操作。"); return; }
                Process.Start(new ProcessStartInfo(netshLogPath) { UseShellExecute = true });
                AppendToolsLog("已打开 netsh 执行日志文件。");
            }
            catch (Exception ex) { AppendToolsLog("❌ 打开日志文件失败: " + ex.Message); }
        }

        /// <summary>
        /// 【新增功能】枚举本机所有 Windows 本地用户账户。用 PowerShell 的 Get-LocalUser 取，
        /// 这个 cmdlet Win10/11 都自带，输出干净，不用额外装包，也不用解析 net user 那种
        /// 容易因为用户名带空格而错位的固定宽度文本表格。
        /// </summary>
        private List<string> GetLocalUserAccounts()
        {
            var result = new List<string>();
            try
            {
                string output = RunPowerShellScript("Get-LocalUser | Select-Object -ExpandProperty Name");
                foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string name = line.Trim();
                    if (!string.IsNullOrEmpty(name)) result.Add(name);
                }
            }
            catch { /* 拿不到就返回空列表，界面上会提示 */ }
            return result;
        }

        /// <summary>【新增功能】更改本机 Windows 用户账户密码：选账户 + 输两遍新密码 + 确认，用 net user 命令改。</summary>
        private void OpenChangePasswordForm()
        {
            Form f = new Form { Text = "更改用户密码", Size = new Size(380, 300), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = colorWhite };

            Label lblUser = new Label { Text = "选择用户:", Location = new Point(20, 30), AutoSize = true, Font = fontNormal };
            ComboBox cboUser = new ComboBox { Location = new Point(110, 27), Size = new Size(220, 25), Font = fontNormal, DropDownStyle = ComboBoxStyle.DropDownList };

            var users = GetLocalUserAccounts();
            if (users.Count == 0) cboUser.Items.Add("(未能获取到本机用户列表)");
            else foreach (var u in users) cboUser.Items.Add(u);
            cboUser.SelectedIndex = 0;

            Label lblPwd = new Label { Text = "新密码:", Location = new Point(20, 75), AutoSize = true, Font = fontNormal };
            TextBox txtPwd = new TextBox { Location = new Point(110, 72), Size = new Size(220, 23), Font = fontNormal, UseSystemPasswordChar = true };

            Label lblPwd2 = new Label { Text = "确认密码:", Location = new Point(20, 115), AutoSize = true, Font = fontNormal };
            TextBox txtPwd2 = new TextBox { Location = new Point(110, 112), Size = new Size(220, 23), Font = fontNormal, UseSystemPasswordChar = true };

            Label lblTip = new Label { Text = "提示：改的是本机 Windows 登录密码，不是路由器/WiFi密码。", Location = new Point(20, 145), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Microsoft YaHei", 8.5F) };

            // 【新增】显示密码复选框，勾上就能看到自己输的是什么，避免打错自己都不知道
            CheckBox chkShowPwd = new CheckBox { Text = "显示密码", Location = new Point(110, 145), AutoSize = true, Font = fontNormal };
            chkShowPwd.CheckedChanged += (s, e) =>
            {
                txtPwd.UseSystemPasswordChar = !chkShowPwd.Checked;
                txtPwd2.UseSystemPasswordChar = !chkShowPwd.Checked;
            };
            lblTip.Location = new Point(20, 170); // 往下挪一点，给显示密码这一行让位

            Button btnOk = new Button { Text = "确认修改", Location = new Point(60, 205), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite, Font = fontBold };
            btnOk.FlatAppearance.BorderSize = 0;
            Button btnCancel = new Button { Text = "取消", Location = new Point(190, 205), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Font = fontBold };
            btnCancel.FlatAppearance.BorderColor = colorBorder;
            btnCancel.Click += (s, e) => f.Close();

            btnOk.Click += async (s, e) =>
            {
                string user = cboUser.SelectedItem?.ToString();
                string pwd = txtPwd.Text;
                string pwd2 = txtPwd2.Text;
                if (string.IsNullOrEmpty(user) || user.StartsWith("(")) { MessageBox.Show("没有可选的用户账户。", "提示"); return; }
                if (pwd != pwd2) { MessageBox.Show("两次输入的密码不一致，请重新输入。", "提示"); return; }
                if (string.IsNullOrEmpty(pwd)) { MessageBox.Show("密码不能为空。", "提示"); return; }

                var confirm = MessageBox.Show($"确定要把账户 \"{user}\" 的密码改成新密码吗？\n改的是本机 Windows 登录密码，改完这个账户下次登录就要用新密码了。", "确认修改", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;

                f.Close();
                AppendToolsLog($"正在修改账户 [{user}] 的密码...");
                string result = await Task.Run(() => RunCmd("net", $"user \"{user}\" \"{pwd}\""));
                bool ok = string.IsNullOrWhiteSpace(result) || result.Contains("成功") || result.IndexOf("completed successfully", StringComparison.OrdinalIgnoreCase) >= 0;
                AppendToolsLog(ok ? $"✅ 账户 [{user}] 的密码已修改成功。" : $"❌ 修改失败: {result.Trim()}");
                MessageBox.Show(ok ? $"账户 \"{user}\" 的密码已修改成功。" : $"修改失败，系统返回:\n{result}", ok ? "完成" : "失败", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            };

            f.Controls.AddRange(new Control[] { lblUser, cboUser, lblPwd, txtPwd, lblPwd2, txtPwd2, chkShowPwd, lblTip, btnOk, btnCancel });
            ApplyDialogDpiScale(f); // 【修复】弹窗独立DPI缩放
            f.ShowDialog(this);
        }

        private void AppendToolsLog(string text)
        {
            if (logTools.InvokeRequired) { logTools.Invoke(new Action(() => AppendToolsLog(text))); return; }
            logTools.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
        }
        #endregion
    }
}
