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
        #region ====== 11.1 网络共享管理 ======
        /// <summary>
        /// 【新增功能】网络共享管理：
        /// 1. 共享文件夹的查看/新建/删除
        /// 2. 一键修复"局域网共享访问不了/看不到"的常见问题（防火墙规则、相关服务、几个关键注册表项、网络类别）
        /// 这部分按你的要求，自动执行不弹确认，只记日志；只有"启用SMB1"这一项涉及真实安全风险
        /// （老旧协议，WannaCry之类勒索病毒利用过的漏洞就在这个协议上），单独留了确认。
        /// </summary>
        private void BuildTabShareLayout()
        {
            tabShare.AutoScroll = true;

            Label lbl = new Label { Text = "网络共享管理", Location = new Point(20, 20), AutoSize = true, Font = fontTitle, ForeColor = colorPrimary };
            tabShare.Controls.Add(lbl);

            // ============ 上半部分：共享文件夹管理 ============
            Label lblShareTitle = new Label { Text = "共享文件夹", Location = new Point(20, 55), AutoSize = true, Font = fontBold, ForeColor = colorPrimary };
            tabShare.Controls.Add(lblShareTitle);

            Button btnRefreshShare = CreateStyledButton("刷新列表", 20, 80, 130, 36, false);
            Button btnNewShare = CreateStyledButton("新建共享", 160, 80, 130, 36);
            Button btnDeleteShare = CreateStyledButton("删除选中共享", 300, 80, 150, 36, false);
            Button btnOpenShareFolder = CreateStyledButton("打开共享文件夹", 460, 80, 160, 36, false);
            tabShare.Controls.AddRange(new Control[] { btnRefreshShare, btnNewShare, btnDeleteShare, btnOpenShareFolder });

            ListView lvShares = new ListView
            {
                Location = new Point(20, 125),
                Size = new Size(860, 180),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                Font = fontNormal
            };
            lvShares.Columns.Add("共享名", 180);
            lvShares.Columns.Add("本地路径", 380);
            lvShares.Columns.Add("说明", 280);
            tabShare.Controls.Add(lvShares);
            AutoStretch(tabShare, lvShares);
            AutoFillLastColumn(lvShares, 200);

            // ============ 下半部分：一键修复共享问题 ============
            Label lblFixTitle = new Label { Text = "共享访问问题修复", Location = new Point(20, 320), AutoSize = true, Font = fontBold, ForeColor = colorPrimary };
            tabShare.Controls.Add(lblFixTitle);

            Button btnFixAll = CreateStyledButton("一键修复共享访问问题", 20, 345, 220, 40);
            Button btnEnableSmb1 = CreateStyledButton("启用SMB1(兼容老设备)", 250, 345, 220, 40, false);
            tabShare.Controls.AddRange(new Control[] { btnFixAll, btnEnableSmb1 });

            Label lblFixDesc = new Label
            {
                Text = "点击\"一键修复\"会自动完成：启用网络发现/文件和打印机共享的防火墙规则、\n" +
                       "启动相关系统服务(Server/Workstation/SSDP等)、修复跨机访问管理员共享被拒绝的注册表项、\n" +
                       "允许访问不需要密码的老旧共享设备、把当前网络类别设为\"专用网络\"（公用网络下共享会被系统限制）。",
                Location = new Point(20, 390), AutoSize = true, ForeColor = Color.Gray, Font = fontNormal
            };
            tabShare.Controls.Add(lblFixDesc);

            TextBox logShare = new TextBox { Location = new Point(20, 445), Size = new Size(860, 200), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = colorConsoleBg, ForeColor = Color.White, Font = new Font("Consolas", 9.5F) };
            logShare.Text = "就绪。";
            tabShare.Controls.Add(logShare);
            AutoStretch(tabShare, logShare);
            void AppendShareLog(string text)
            {
                if (logShare.InvokeRequired) { logShare.Invoke(new Action(() => AppendShareLog(text))); return; }
                logShare.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
            }

            // ---- 共享列表加载/刷新 ----
            // 【性能修复】GetSmbShares() 内部要拉起一个 powershell.exe 进程执行 Get-SmbShare，
            // PowerShell 宿主启动本身就要几百毫秒到一两秒。原来这里是同步调用，且在窗体构造函数里
            // (BuildModernLayout -> BuildTabShareLayout) 直接执行一次，相当于窗口画面还没显示出来，
            // 就先在 UI 线程上死等这个 powershell 进程跑完——这是整个程序"打开慢"的主要原因之一。
            // 改成 async：真正耗时的 GetSmbShares() 丢到后台线程跑，UI 线程只负责把结果显示出来。
            async Task LoadSharesAsync()
            {
                lvShares.Items.Clear();
                var shares = await Task.Run(() => GetSmbShares());
                lvShares.Items.Clear();
                foreach (var sh in shares)
                {
                    var item = new ListViewItem(new[] { sh.Name, sh.Path, sh.Description });
                    item.Tag = sh;
                    lvShares.Items.Add(item);
                }
            }
            btnRefreshShare.Click += async (s, e) => await LoadSharesAsync();
            // 【性能修复】不再在构造函数里同步阻塞加载，窗口先正常显示出来，加载动作丢到后台异步执行，
            // 用 "_ = " 明确表示这是"启动即触发、不等待"的后台任务，跟其它状态看板的刷新写法保持一致。
            _ = LoadSharesAsync();

            // ---- 新建共享 ----
            btnNewShare.Click += (s, e) => OpenNewShareDialog(async () => { await LoadSharesAsync(); AppendShareLog("已刷新共享列表。"); }, AppendShareLog);

            // ---- 删除共享 ----
            btnDeleteShare.Click += async (s, e) =>
            {
                if (lvShares.SelectedItems.Count == 0) { MessageBox.Show("请先选中一个共享。", "提示"); return; }
                var sh = (SmbShareInfo)lvShares.SelectedItems[0].Tag;
                if (sh.Name.EndsWith("$"))
                {
                    var warn = MessageBox.Show($"\"{sh.Name}\" 看起来是系统管理共享（C$/ADMIN$/IPC$这类），一般不建议删除，可能影响远程管理功能。\n真的要删除吗？", "注意", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (warn != DialogResult.Yes) return;
                }
                else
                {
                    var confirm = MessageBox.Show($"确定要删除共享 \"{sh.Name}\" 吗？（只是取消共享，不会删除本地文件本身）", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (confirm != DialogResult.Yes) return;
                }
                string result = await Task.Run(() => RunPowerShellScript($"Remove-SmbShare -Name '{sh.Name}' -Force"));
                AppendShareLog(string.IsNullOrWhiteSpace(result) ? $"✅ 已删除共享: {sh.Name}" : $"删除结果: {result.Trim()}");
                await LoadSharesAsync();
            };

            btnOpenShareFolder.Click += (s, e) =>
            {
                if (lvShares.SelectedItems.Count == 0) { MessageBox.Show("请先选中一个共享。", "提示"); return; }
                var sh = (SmbShareInfo)lvShares.SelectedItems[0].Tag;
                if (string.IsNullOrWhiteSpace(sh.Path) || !Directory.Exists(sh.Path)) { MessageBox.Show("找不到这个共享对应的本地文件夹。", "提示"); return; }
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{sh.Path}\"") { UseShellExecute = true }); } catch { }
            };

            // ---- 一键修复（自动执行，不弹确认，全程记日志） ----
            btnFixAll.Click += async (s, e) =>
            {
                btnFixAll.Enabled = false;
                AppendShareLog("========== 开始一键修复共享访问问题 ==========");
                await Task.Run(() =>
                {
                    void Run(string tag, string exe, string args)
                    {
                        string r = RunCmd(exe, args);
                        AppendShareLog($"[{tag}] {(string.IsNullOrWhiteSpace(r) ? "完成" : r.Trim())}");
                    }

                    Run("防火墙-网络发现", "netsh", "advfirewall firewall set rule group=\"网络发现\" new enable=yes");
                    Run("防火墙-文件和打印机共享", "netsh", "advfirewall firewall set rule group=\"文件和打印机共享\" new enable=yes");

                    foreach (var svc in new[] { "LanmanServer", "LanmanWorkstation", "FDResPub", "FDPHOST", "SSDPSRV", "upnphost" })
                    {
                        RunCmd("sc", $"config {svc} start=auto");
                        Run($"启动服务-{svc}", "net", $"start {svc}");
                    }

                    // 修复跨机(工作组环境下)访问管理员共享(C$等)经常遇到的"拒绝访问"问题
                    try
                    {
                        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                        {
                            key.SetValue("LocalAccountTokenFilterPolicy", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        }
                        AppendShareLog("[注册表] LocalAccountTokenFilterPolicy=1（修复工作组环境下访问管理员共享被拒绝的问题）");
                    }
                    catch (Exception ex) { AppendShareLog("[注册表] 修改LocalAccountTokenFilterPolicy失败: " + ex.Message); }

                    // 允许连接不需要密码/身份验证受限的老旧共享设备(常见于连老NAS/老路由器共享失败)
                    try
                    {
                        using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters"))
                        {
                            key.SetValue("AllowInsecureGuestAuth", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        }
                        AppendShareLog("[注册表] AllowInsecureGuestAuth=1（允许访问不需要密码验证的老旧共享设备）");
                    }
                    catch (Exception ex) { AppendShareLog("[注册表] 修改AllowInsecureGuestAuth失败: " + ex.Message); }

                    // 把当前网络类别设为"专用网络"——"公用网络"下 Windows 会默认限制网络发现和共享
                    string catResult = RunPowerShellScript("Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private");
                    AppendShareLog($"[网络类别] 已尝试设为\"专用网络\" {(string.IsNullOrWhiteSpace(catResult) ? "(完成)" : catResult.Trim())}");
                });
                AppendShareLog("========== 修复完成，建议重启一下资源管理器或重新登录生效更彻底 ==========");
                btnFixAll.Enabled = true;
            };

            // ---- 启用SMB1（单独保留确认，因为涉及真实安全风险） ----
            btnEnableSmb1.Click += async (s, e) =>
            {
                var confirm = MessageBox.Show(
                    "SMB1 是一个已经过时、存在已知安全漏洞的老协议（\"永恒之蓝\"勒索病毒利用的就是这个协议的漏洞），\n" +
                    "只有确实需要连接非常老旧的NAS/打印机/路由器共享（新协议连不上）时才建议开启，用完最好再关掉。\n\n" +
                    "确定要启用SMB1吗？",
                    "安全提醒", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;

                btnEnableSmb1.Enabled = false;
                AppendShareLog("正在启用SMB1协议(可能需要重启电脑才能完全生效)...");
                string r = await Task.Run(() => RunCmd("dism", "/online /Norestart /Enable-Feature /FeatureName:SMB1Protocol /All"));
                AppendShareLog(string.IsNullOrWhiteSpace(r) ? "✅ SMB1启用命令已执行。" : r.Trim());
                btnEnableSmb1.Enabled = true;
            };
        }

        /// <summary>SMB共享的基本信息。</summary>
        private class SmbShareInfo
        {
            public string Name;
            public string Path;
            public string Description;
        }

        /// <summary>枚举本机所有SMB共享（用 PowerShell 的 Get-SmbShare，比解析 net share 的表格文本稳妥）。</summary>
        private List<SmbShareInfo> GetSmbShares()
        {
            var results = new List<SmbShareInfo>();
            try
            {
                // 用一个不常见的分隔符拼行，避免共享名/路径/说明里万一带逗号导致按逗号解析错位
                string script = "Get-SmbShare | ForEach-Object { \"$($_.Name)|||$($_.Path)|||$($_.Description)\" }";
                string output = RunPowerShellScript(script);
                foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(new[] { "|||" }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        results.Add(new SmbShareInfo { Name = parts[0].Trim(), Path = parts[1].Trim(), Description = parts.Length > 2 ? parts[2].Trim() : "" });
                    }
                }
            }
            catch { /* 拿不到就返回空列表 */ }
            return results;
        }

        /// <summary>新建共享的弹窗：选文件夹 + 填共享名 + 选权限级别。</summary>
        private void OpenNewShareDialog(Action onSuccess, Action<string> log)
        {
            Form f = new Form { Text = "新建共享文件夹", Size = new Size(420, 300), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = colorWhite };

            Label lblPath = new Label { Text = "共享文件夹:", Location = new Point(20, 25), AutoSize = true, Font = fontNormal };
            TextBox txtPath = new TextBox { Location = new Point(120, 22), Size = new Size(190, 23), Font = fontNormal, ReadOnly = true };
            Button btnBrowse = new Button { Text = "浏览...", Location = new Point(318, 21), Size = new Size(70, 25), FlatStyle = FlatStyle.Flat };
            btnBrowse.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    if (fbd.ShowDialog() == DialogResult.OK) txtPath.Text = fbd.SelectedPath;
                }
            };

            Label lblName = new Label { Text = "共享名称:", Location = new Point(20, 65), AutoSize = true, Font = fontNormal };
            TextBox txtName = new TextBox { Location = new Point(120, 62), Size = new Size(268, 23), Font = fontNormal };

            Label lblDesc = new Label { Text = "备注说明:", Location = new Point(20, 105), AutoSize = true, Font = fontNormal };
            TextBox txtDesc = new TextBox { Location = new Point(120, 102), Size = new Size(268, 23), Font = fontNormal };

            Label lblPerm = new Label { Text = "访问权限:", Location = new Point(20, 145), AutoSize = true, Font = fontNormal };
            ComboBox cboPerm = new ComboBox { Location = new Point(120, 142), Size = new Size(268, 25), Font = fontNormal, DropDownStyle = ComboBoxStyle.DropDownList };
            cboPerm.Items.AddRange(new object[] { "所有人 - 只读", "所有人 - 完全控制(可读写)" });
            cboPerm.SelectedIndex = 0;

            Button btnOk = new Button { Text = "创建", Location = new Point(90, 200), Size = new Size(110, 36), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite, Font = fontBold };
            btnOk.FlatAppearance.BorderSize = 0;
            Button btnCancel = new Button { Text = "取消", Location = new Point(220, 200), Size = new Size(110, 36), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Font = fontBold };
            btnCancel.FlatAppearance.BorderColor = colorBorder;
            btnCancel.Click += (s, e) => f.Close();

            btnOk.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtPath.Text) || !Directory.Exists(txtPath.Text)) { MessageBox.Show("请选择一个有效的文件夹。", "提示"); return; }
                if (string.IsNullOrWhiteSpace(txtName.Text)) { MessageBox.Show("请填写共享名称。", "提示"); return; }

                string permArg = cboPerm.SelectedIndex == 1 ? "-FullAccess Everyone" : "-ReadAccess Everyone";
                string script = $"New-SmbShare -Name '{txtName.Text.Trim()}' -Path '{txtPath.Text}' -Description '{txtDesc.Text.Trim()}' {permArg}";
                f.Close();
                string result = await Task.Run(() => RunPowerShellScript(script));
                bool ok = string.IsNullOrWhiteSpace(result) || result.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0;
                log(ok ? $"✅ 共享已创建: {txtName.Text.Trim()} → {txtPath.Text}" : $"❌ 创建共享失败: {result.Trim()}");
                onSuccess();
            };

            f.Controls.AddRange(new Control[] { lblPath, txtPath, btnBrowse, lblName, txtName, lblDesc, txtDesc, lblPerm, cboPerm, btnOk, btnCancel });
            ApplyDialogDpiScale(f);
            f.ShowDialog(this);
        }
        #endregion
    }
}
