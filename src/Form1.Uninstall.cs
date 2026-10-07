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
        #region ====== 11.3 软件卸载(深度版) ======
        /// <summary>
        /// 【新增功能】软件卸载（深度版）：先枚举本机已安装的软件，选中后调用软件自带的卸载程序卸载，
        /// 卸载完成后再扫描一遍常见的软件数据目录（Program Files / ProgramData / AppData等），
        /// 把跟这个软件名字匹配的残留文件夹列出来，让你自己确认之后再删——不会不打招呼就乱删任何东西。
        /// </summary>
        private void BuildTabUninstallLayout()
        {
            // 【修复】之前按钮放在列表下面，列表一多、内容总高度超出Tab可视区域，
            // 按钮就被顶到看不见的地方去了，点不到也拖不到。这次把按钮挪到列表上方，
            // 永远可见；另外给Tab页加上 AutoScroll 兜底，双重保险。
            tabUninstall.AutoScroll = true;

            Label lbl = new Label { Text = "软件卸载（深度清理版）", Location = new Point(20, 20), AutoSize = true, Font = fontTitle, ForeColor = colorPrimary };
            tabUninstall.Controls.Add(lbl);

            Label lblTip = new Label { Text = "提示：先用软件自带的卸载程序正常卸载，卸载完再扫描并清理残留的缓存/配置目录。左键选中一行，也可以直接右键弹出菜单操作。", Location = new Point(20, 50), AutoSize = true, ForeColor = Color.Gray, Font = fontNormal };
            tabUninstall.Controls.Add(lblTip);

            Button btnRefresh = CreateStyledButton("刷新列表", 20, 75, 140, 38, false);
            Button btnUninstall = CreateStyledButton("卸载选中的软件", 170, 75, 160, 38);
            Button btnScanOnly = CreateStyledButton("仅扫描残留(不卸载)", 340, 75, 180, 38, false);
            Label lblUninstallStatus = new Label { Text = "", Location = new Point(530, 85), AutoSize = true, ForeColor = colorPrimary, Font = fontBold };
            tabUninstall.Controls.AddRange(new Control[] { btnRefresh, btnUninstall, btnScanOnly, lblUninstallStatus });

            ListView lvApps = new ListView
            {
                Location = new Point(20, 120),
                Size = new Size(860, 300),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                Font = fontNormal
            };
            lvApps.Columns.Add("软件名称", 320);
            lvApps.Columns.Add("发布者", 220);
            lvApps.Columns.Add("版本", 120);
            lvApps.Columns.Add("安装位置", 180);
            tabUninstall.Controls.Add(lvApps);
            AutoStretch(tabUninstall, lvApps);
            AutoFillLastColumn(lvApps, 150);

            // 【新增】卸载执行日志——之前"弹窗正常但没反应"这种情况完全看不出原因，
            // 加个日志区，把实际解析出来执行的命令、退出码都记下来，以后再遇到类似问题一眼能看出来。
            Label lblLogTag = new Label { Text = "执行日志：", Location = new Point(20, 428), AutoSize = true, ForeColor = Color.Gray, Font = fontNormal };
            tabUninstall.Controls.Add(lblLogTag);
            TextBox logUninstall = new TextBox { Location = new Point(20, 450), Size = new Size(860, 130), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = colorConsoleBg, ForeColor = Color.White, Font = new Font("Consolas", 9.5F) };
            logUninstall.Text = "就绪。";
            tabUninstall.Controls.Add(logUninstall);
            AutoStretch(tabUninstall, logUninstall);
            void AppendUninstallLog(string text)
            {
                if (logUninstall.InvokeRequired) { logUninstall.Invoke(new Action(() => AppendUninstallLog(text))); return; }
                logUninstall.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
            }

            // 【新增】右键菜单，符合大部分人对"卸载软件"这类工具的操作直觉
            var ctxMenu = new ContextMenuStrip();
            var ctxUninstall = new ToolStripMenuItem("卸载此软件");
            var ctxScanOnly = new ToolStripMenuItem("仅扫描残留文件(不卸载)");
            var ctxOpenLocation = new ToolStripMenuItem("打开安装目录");
            ctxMenu.Items.AddRange(new ToolStripItem[] { ctxUninstall, ctxScanOnly, ctxOpenLocation });
            lvApps.ContextMenuStrip = ctxMenu;

            // 右键点在哪一行，就先选中那一行，再弹菜单，避免"右键的行"和"选中的行"不一致
            lvApps.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var hit = lvApps.HitTest(e.Location);
                if (hit.Item != null) hit.Item.Selected = true;
            };

            var programList = new List<InstalledProgramInfo>();

            // 【性能修复】GetInstalledPrograms() 要逐个打开、遍历 HKLM/HKCU 好几个 Uninstall 注册表项
            // 下面所有的子键，软件装得越多耗时越明显。原来是同步方法，且在窗体构造函数里
            // (BuildModernLayout -> BuildTabUninstallLayout) 就直接调用了一次——窗口还没显示出来，
            // UI 线程就先卡在这里扫描注册表，是"打开慢"的另一个主要原因。
            // 改成 async：扫描放到后台线程，扫完再回 UI 线程刷新列表。
            async Task LoadProgramsAsync()
            {
                lblUninstallStatus.Text = "正在加载已安装软件列表...";
                var loaded = await Task.Run(() => GetInstalledPrograms());
                lvApps.Items.Clear();
                programList.Clear();
                programList.AddRange(loaded);
                foreach (var p in programList)
                {
                    var item = new ListViewItem(new[] { p.Name, p.Publisher, p.Version, p.InstallLocation });
                    item.Tag = p;
                    lvApps.Items.Add(item);
                }
                lblUninstallStatus.Text = $"共 {programList.Count} 个已安装软件";
            }

            btnRefresh.Click += async (s, e) => await LoadProgramsAsync();
            // 【性能修复】不再同步阻塞构造函数，改成启动即触发、不等待的后台加载。
            _ = LoadProgramsAsync(); // 打开页面就先加载一次（异步，不卡界面）

            async Task DoUninstall()
            {
                if (lvApps.SelectedItems.Count == 0) { MessageBox.Show("请先在列表里选中要卸载的软件。", "提示"); return; }
                var info = (InstalledProgramInfo)lvApps.SelectedItems[0].Tag;

                var confirm = MessageBox.Show(
                    $"确定要卸载 \"{info.Name}\" 吗？\n\n将调用该软件自带的卸载程序，卸载完成后会自动扫描一遍残留文件（不会自动删，会先列出来给你确认）。",
                    "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;

                btnUninstall.Enabled = false; btnRefresh.Enabled = false;
                lblUninstallStatus.Text = $"正在卸载 {info.Name}...";

                // 【关键修复】之前直接把 UninstallString 整串丢给 "cmd /c" 执行——
                // 但有些软件的卸载命令是"未加引号的带空格路径"，比如：
                //   C:\Program Files\SomeApp\uninst.exe -s
                // cmd 解析命令行是按空格分词的，遇到这种没加引号的路径，会在第一个空格处
                // 把命令错误截断成 "C:\Program"，导致"文件找不到"，静默失败，界面上完全看不出来。
                // 改成自己解析出真正的可执行文件路径 + 参数，直接启动这个可执行文件，
                // 不再经过 cmd 这一层转发和它的分词规则。
                var (exeFile, exeArgs) = ParseUninstallCommand(info.UninstallString);
                AppendUninstallLog($"卸载命令原文: {info.UninstallString}");
                AppendUninstallLog($"解析结果 → 程序: \"{exeFile}\"  参数: \"{exeArgs}\"");

                // 校验解析出来的可执行文件路径靠不靠谱：
                // - 如果是完整路径，检查文件是否真的存在
                // - 如果只是个文件名（没有路径分隔符，比如 MsiExec.exe / rundll32.exe），
                //   这种是指望系统按 PATH 环境变量去找，不能用 File.Exists 直接判断存不存在，放行即可
                bool looksLikeBarePath = exeFile.IndexOf('\\') < 0 && exeFile.IndexOf('/') < 0;
                if (string.IsNullOrWhiteSpace(exeFile) || (!looksLikeBarePath && !File.Exists(exeFile)))
                {
                    AppendUninstallLog($"❌ 解析出的卸载程序路径不存在，放弃执行: {exeFile}");
                    MessageBox.Show($"没能正确解析出卸载程序的路径，卸载命令原文是：\n{info.UninstallString}\n\n可以把这段发给我，我帮你看看怎么处理这种特殊格式。", "解析失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnUninstall.Enabled = true; btnRefresh.Enabled = true;
                    lblUninstallStatus.Text = "";
                    return;
                }

                int exitCode = -1;
                try
                {
                    await Task.Run(() =>
                    {
                        var psi = new ProcessStartInfo(exeFile, exeArgs) { UseShellExecute = true };
                        using (var proc = Process.Start(psi))
                        {
                            proc?.WaitForExit();
                            exitCode = proc?.ExitCode ?? -1;
                        }
                    });
                    AppendUninstallLog($"卸载程序已退出，退出码: {exitCode}（大部分安装程序 0 代表成功，但也有软件用别的约定，仅供参考）");
                }
                catch (Exception ex)
                {
                    AppendUninstallLog("❌ 启动卸载程序失败: " + ex.Message);
                    MessageBox.Show("启动卸载程序失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnUninstall.Enabled = true; btnRefresh.Enabled = true;
                    lblUninstallStatus.Text = "";
                    return;
                }

                lblUninstallStatus.Text = "卸载程序已退出，正在扫描残留文件...";
                var residuals = await Task.Run(() => FindResidualFolders(info.Name));

                if (residuals.Count == 0)
                {
                    MessageBox.Show($"\"{info.Name}\" 卸载流程已结束，没有扫描到明显的残留文件夹。", "完成");
                }
                else
                {
                    ShowResidualCleanupDialog(info.Name, residuals);
                }

                lblUninstallStatus.Text = "";
                btnUninstall.Enabled = true; btnRefresh.Enabled = true;
                await LoadProgramsAsync(); // 卸载完刷新一下列表
            }

            async Task DoScanOnly()
            {
                if (lvApps.SelectedItems.Count == 0) { MessageBox.Show("请先在列表里选中一个软件。", "提示"); return; }
                var info = (InstalledProgramInfo)lvApps.SelectedItems[0].Tag;
                lblUninstallStatus.Text = $"正在扫描 {info.Name} 的残留文件...";
                var residuals = await Task.Run(() => FindResidualFolders(info.Name));
                lblUninstallStatus.Text = "";
                if (residuals.Count == 0)
                    MessageBox.Show($"没有扫描到 \"{info.Name}\" 相关的残留文件夹。", "完成");
                else
                    ShowResidualCleanupDialog(info.Name, residuals);
            }

            btnUninstall.Click += async (s, e) => await DoUninstall();
            btnScanOnly.Click += async (s, e) => await DoScanOnly();
            ctxUninstall.Click += async (s, e) => await DoUninstall();
            ctxScanOnly.Click += async (s, e) => await DoScanOnly();
            ctxOpenLocation.Click += (s, e) =>
            {
                if (lvApps.SelectedItems.Count == 0) return;
                var info = (InstalledProgramInfo)lvApps.SelectedItems[0].Tag;
                if (string.IsNullOrWhiteSpace(info.InstallLocation) || !Directory.Exists(info.InstallLocation))
                {
                    MessageBox.Show("这个软件没有记录安装目录，或者目录已经不存在了。", "提示");
                    return;
                }
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{info.InstallLocation}\"") { UseShellExecute = true }); }
                catch { /* 打开失败就算了 */ }
            };
        }

        /// <summary>已安装软件的基本信息（从注册表读出来的）。</summary>
        private class InstalledProgramInfo
        {
            public string Name;
            public string Publisher;
            public string Version;
            public string InstallLocation;
            public string UninstallString;
        }

        /// <summary>枚举本机所有已安装软件（同时读 64位/32位/当前用户 三处注册表位置，去掉没有卸载命令的系统组件）。</summary>
        /// <summary>
        /// 把注册表里的 UninstallString 解析成"可执行文件路径"+"参数"两部分，用于直接启动，
        /// 不再依赖 cmd /c 转发（cmd 按空格分词，遇到没加引号、路径本身又带空格的命令会解析错误）。
        /// </summary>
        private (string fileName, string args) ParseUninstallCommand(string cmdLine)
        {
            cmdLine = cmdLine.Trim();
            if (cmdLine.StartsWith("\""))
            {
                int endQuote = cmdLine.IndexOf('"', 1);
                if (endQuote > 0)
                    return (cmdLine.Substring(1, endQuote - 1), cmdLine.Substring(endQuote + 1).Trim());
            }

            // 【关键修复】先看看整串（完全不拆分）本身是不是就是一个真实存在的文件——
            // 有些卸载路径本身就带空格（比如 "D:\Program Files\App\uninstall.exe"），但后面
            // 根本没有任何额外参数，这种情况下不该拆，一拆就会在文件夹名字自带的空格处切错。
            if (File.Exists(cmdLine)) return (cmdLine, "");

            // 没加引号：从左到右尝试每个空格位置，看能不能拼出一个真实存在的文件路径
            // （用于处理"路径带空格 + 后面还跟着参数"的情况，比如 D:\App\uninst.exe -s）
            int idx = 0;
            while (true)
            {
                int nextSpace = cmdLine.IndexOf(' ', idx);
                if (nextSpace < 0) break;
                string candidate = cmdLine.Substring(0, nextSpace);
                if (File.Exists(candidate))
                    return (candidate, cmdLine.Substring(nextSpace + 1).Trim());
                idx = nextSpace + 1;
            }
            // 没有任何前缀命中真实文件（常见于 MsiExec.exe 这种靠系统PATH解析、不是绝对路径的情况），
            // 退回最简单的"按第一个空格切"，把第一段当程序名、其余当参数。
            int firstSpace = cmdLine.IndexOf(' ');
            if (firstSpace > 0)
                return (cmdLine.Substring(0, firstSpace), cmdLine.Substring(firstSpace + 1).Trim());
            return (cmdLine, "");
        }

        private List<InstalledProgramInfo> GetInstalledPrograms()
        {
            var results = new List<InstalledProgramInfo>();
            var seenNames = new HashSet<string>();

            void ScanKey(Microsoft.Win32.RegistryKey root, string path)
            {
                using (var key = root.OpenSubKey(path))
                {
                    if (key == null) return;
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;
                                string name = subKey.GetValue("DisplayName") as string;
                                if (string.IsNullOrWhiteSpace(name)) continue;

                                // 跳过"系统组件"（补丁、运行库这些不该在这里手动卸载的东西）
                                int.TryParse(subKey.GetValue("SystemComponent")?.ToString(), out int isSystemComponent);
                                if (isSystemComponent == 1) continue;

                                string uninstallStr = subKey.GetValue("UninstallString") as string;
                                if (string.IsNullOrWhiteSpace(uninstallStr)) continue; // 没有卸载命令的跳过，卸不了

                                if (!seenNames.Add(name)) continue; // 同名的（32/64位重复出现）只留一条

                                results.Add(new InstalledProgramInfo
                                {
                                    Name = name,
                                    Publisher = subKey.GetValue("Publisher") as string ?? "",
                                    Version = subKey.GetValue("DisplayVersion") as string ?? "",
                                    InstallLocation = subKey.GetValue("InstallLocation") as string ?? "",
                                    UninstallString = uninstallStr
                                });
                            }
                        }
                        catch { /* 单个软件注册表项读取失败就跳过，不影响其它的 */ }
                    }
                }
            }

            ScanKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            return results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 在几个常见的软件数据存放目录（Program Files / ProgramData / AppData Roaming / AppData Local）
        /// 里找名字包含软件名关键字的文件夹，作为"可能的残留"列出来。只扫一层，不深入递归，
        /// 兼顾速度和"不要误伤太多不相关文件夹"。
        /// </summary>
        private List<string> FindResidualFolders(string appName)
        {
            var found = new List<string>();
            string keyword = appName.Trim();
            if (keyword.Length < 2) return found; // 名字太短容易大范围误伤，不扫

            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // ProgramData
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),       // AppData\Roaming
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),  // AppData\Local
            };

            foreach (var root in roots.Distinct())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        string name = Path.GetFileName(dir);
                        if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            found.Add(dir);
                    }
                }
                catch { /* 目录访问失败就跳过 */ }
            }
            return found;
        }

        /// <summary>弹窗展示扫描到的残留文件夹，勾选后统一删除。</summary>
        private void ShowResidualCleanupDialog(string appName, List<string> residuals)
        {
            Form f = new Form { Text = $"\"{appName}\" 的残留文件", Size = new Size(700, 480), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = colorWhite };

            Label lblHint = new Label { Text = "扫描到以下可能的残留文件夹，勾选要删除的，确认后再统一清理（未勾选的不会动）：", Location = new Point(15, 15), AutoSize = true, Font = fontNormal };

            CheckedListBox clb = new CheckedListBox { Location = new Point(15, 45), Size = new Size(655, 330), Font = fontNormal, CheckOnClick = true };
            foreach (var r in residuals) clb.Items.Add(r, true); // 默认全部勾选，用户自己去掉不想删的

            Button btnDeleteSelected = new Button { Text = "删除勾选的文件夹", Location = new Point(15, 390), Size = new Size(160, 36), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite, Font = fontBold };
            btnDeleteSelected.FlatAppearance.BorderSize = 0;
            Button btnSkip = new Button { Text = "都不删，关闭", Location = new Point(185, 390), Size = new Size(140, 36), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Font = fontBold };
            btnSkip.FlatAppearance.BorderColor = colorBorder;
            btnSkip.Click += (s, e) => f.Close();

            btnDeleteSelected.Click += async (s, e) =>
            {
                var toDelete = new List<string>();
                for (int i = 0; i < clb.Items.Count; i++)
                    if (clb.GetItemChecked(i)) toDelete.Add((string)clb.Items[i]);

                if (toDelete.Count == 0) { MessageBox.Show("没有勾选任何文件夹。", "提示"); return; }

                var confirm = MessageBox.Show($"确定要删除这 {toDelete.Count} 个文件夹吗？此操作不可恢复。", "最终确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;

                f.Close();
                int success = 0, failed = 0;
                await Task.Run(() =>
                {
                    foreach (var dir in toDelete)
                    {
                        try { Directory.Delete(dir, true); success++; }
                        catch { failed++; }
                    }
                });
                MessageBox.Show($"清理完成：成功删除 {success} 个，失败 {failed} 个（失败的通常是文件正被占用，可以重启电脑后再手动删除）。", "完成");
            };

            f.Controls.AddRange(new Control[] { lblHint, clb, btnDeleteSelected, btnSkip });
            ApplyDialogDpiScale(f);
            f.ShowDialog(this);
        }
        #endregion
    }
}
