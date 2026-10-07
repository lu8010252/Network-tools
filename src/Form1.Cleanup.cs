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
        #region ====== 11.4 C盘深度安全清理 ======
        private void BuildTabCleanLayout()
        {
            Label lbl = new Label { Text = "C盘深度安全清理中心", Location = new Point(20, 20), AutoSize = true, Font = fontTitle, ForeColor = colorPrimary };

            Button btnScanJunk = CreateStyledButton("扫描垃圾大小", 20, 55, 160, 38, false);
            btnScanJunk.Click += async (s, e) => await ScanJunkSizeAsync();

            Button btnCleanTemp = CreateStyledButton("清理临时文件", 190, 55, 160, 38, false);
            btnCleanTemp.Click += async (s, e) =>
            {
                var confirm = MessageBox.Show("将清理系统临时文件夹(%TEMP% 及 C:\\Windows\\Temp)中的文件，正在被占用的文件会自动跳过。\n确定继续吗？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
                await CleanTempFilesAsync();
            };

            Button btnEmptyRecycle = CreateStyledButton("清空回收站", 360, 55, 160, 38, false);
            btnEmptyRecycle.Click += (s, e) =>
            {
                var confirm = MessageBox.Show("确定要清空回收站吗？此操作不可恢复。", "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
                try
                {
                    int ret = SHEmptyRecycleBin(this.Handle, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                    AppendCleanLog(ret == 0 ? "✅ 回收站已清空。" : $"清空完成（返回码: {ret}，非0大概率是回收站本来就是空的，不代表出错）。");
                }
                catch (Exception ex) { AppendCleanLog("❌ 清空失败: " + ex.Message); }
            };

            Button btnCleanWU = CreateStyledButton("清理Windows更新缓存", 530, 55, 190, 38, false);
            btnCleanWU.Click += async (s, e) =>
            {
                var confirm = MessageBox.Show("将停止 Windows Update 服务并清理更新下载缓存(C:\\Windows\\SoftwareDistribution\\Download)，完成后会自动重启该服务。\n确定继续吗？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
                await CleanWindowsUpdateCacheAsync();
            };

            // 【新增】第二排：浏览器缓存/缩略图缓存/Prefetch预读取文件/安装包缓存，
            // 都是常见"电脑管家类"软件会清理的东西，选的都是浏览器/系统会自动重新生成的缓存文件，
            // 删了不影响正常使用，只是下次打开对应功能时会稍微慢一点点（重新生成缓存而已）。
            Button btnCleanBrowser = CreateStyledButton("清理浏览器缓存", 20, 100, 160, 38, false);
            btnCleanBrowser.Click += async (s, e) =>
            {
                var confirm = MessageBox.Show("将清理 Edge / Chrome 浏览器的缓存文件（不影响收藏夹、密码、历史记录）。\n建议先关闭浏览器再清理，效果更彻底。\n确定继续吗？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
                await CleanBrowserCacheAsync();
            };

            Button btnCleanThumb = CreateStyledButton("清理缩略图缓存", 190, 100, 160, 38, false);
            btnCleanThumb.Click += async (s, e) =>
            {
                var confirm = MessageBox.Show("将清理图片/文件夹缩略图缓存，删除后系统会自动重新生成，不影响原文件。\n确定继续吗？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
                await CleanThumbnailCacheAsync();
            };

            Button btnCleanPrefetch = CreateStyledButton("清理Prefetch文件", 360, 100, 160, 38, false);
            btnCleanPrefetch.Click += async (s, e) =>
            {
                var confirm = MessageBox.Show("将清理系统预读取文件(C:\\Windows\\Prefetch)，Windows会自动重新生成，不影响系统运行。\n确定继续吗？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
                await CleanPrefetchAsync();
            };

            Button btnCleanPkgCache = CreateStyledButton("清理安装包缓存", 530, 100, 190, 38, false);
            btnCleanPkgCache.Click += async (s, e) =>
            {
                var confirm = MessageBox.Show("将清理 C:\\ProgramData\\Package Cache（各软件MSI安装包留下的缓存副本）。\n⚠️ 注意：清理后如果日后要\"修复\"或\"卸载\"某些用MSI安装的软件，系统可能会要求你重新提供原始安装包，其余情况不受影响。\n确定继续吗？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
                await CleanPackageCacheAsync();
            };

            // 【新增】大文件扫描：只扫描列出来，不自动删——大文件很可能是你自己的重要资料（视频/安装包/备份），
            // 不该由程序自作主张删掉，交给你自己看着办，需要的话可以照着路径去手动清理。
            Button btnFindLargeFiles = CreateStyledButton("查找C盘大文件(>300MB)", 20, 145, 230, 38, false);
            btnFindLargeFiles.Click += async (s, e) => await FindLargeFilesAsync();

            logClean = new TextBox { Location = new Point(20, 195), Size = new Size(860, 345), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = colorConsoleBg, ForeColor = Color.White, Font = new Font("Consolas", 10F) };
            logClean.Text = "全自动化全盘冗余深度垃圾扫描就绪。点击上方按钮开始操作。";
            tabClean.Controls.AddRange(new Control[] {
                lbl, btnScanJunk, btnCleanTemp, btnEmptyRecycle, btnCleanWU,
                btnCleanBrowser, btnCleanThumb, btnCleanPrefetch, btnCleanPkgCache,
                btnFindLargeFiles, logClean
            });
            AutoStretch(tabClean, logClean, width: true, height: true, rightMargin: 15, bottomMargin: 15);
        }

        private void AppendCleanLog(string text)
        {
            if (logClean.InvokeRequired) { logClean.Invoke(new Action(() => AppendCleanLog(text))); return; }
            logClean.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
        }

        private long GetDirSize(string path)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(path)) return 0;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch { /* 跳过无法访问的文件 */ }
                }
            }
            catch { /* 目录本身无法访问就跳过 */ }
            return total;
        }

        private string FormatBytes(long bytes)
        {
            double mb = bytes / 1024.0 / 1024.0;
            return mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";
        }

        private async Task ScanJunkSizeAsync()
        {
            AppendCleanLog("正在扫描各项垃圾大小，请稍候...");
            await Task.Run(() =>
            {
                string tempPath = Path.GetTempPath();
                string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                string wuCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");

                long tempSize = GetDirSize(tempPath);
                long winTempSize = GetDirSize(winTemp);
                long wuSize = GetDirSize(wuCache);

                AppendCleanLog($"用户临时文件夹 ({tempPath}): {FormatBytes(tempSize)}");
                AppendCleanLog($"系统临时文件夹 (C:\\Windows\\Temp): {FormatBytes(winTempSize)}");
                AppendCleanLog($"Windows更新缓存: {FormatBytes(wuSize)}");
                AppendCleanLog($"合计可清理约: {FormatBytes(tempSize + winTempSize + wuSize)} (回收站大小另需系统API单独查询，未计入)");
            });
        }

        private async Task CleanTempFilesAsync()
        {
            AppendCleanLog("正在清理临时文件...");
            await Task.Run(() =>
            {
                int deletedFiles = 0, skipped = 0;
                long freedBytes = 0;
                foreach (var dir in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in SafeEnumerateFiles(dir))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            long len = fi.Length;
                            fi.Delete();
                            deletedFiles++; freedBytes += len;
                        }
                        catch { skipped++; } // 文件被占用/无权限，跳过即可，不中断整体清理
                    }
                }
                AppendCleanLog($"✅ 清理完成：删除 {deletedFiles} 个文件，释放约 {FormatBytes(freedBytes)}；跳过 {skipped} 个正被占用/无权限的文件。");
            });
        }

        private IEnumerable<string> SafeEnumerateFiles(string dir)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); }
            catch { yield break; }
            foreach (var f in files) yield return f;
        }

        private async Task CleanWindowsUpdateCacheAsync()
        {
            AppendCleanLog("正在停止 Windows Update 服务...");
            string wuCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            await Task.Run(() =>
            {
                string stopResult = RunCmd("net", "stop wuauserv");
                AppendCleanLog(string.IsNullOrWhiteSpace(stopResult) ? "服务已停止。" : stopResult.Trim());

                int deleted = 0; long freed = 0;
                foreach (var file in SafeEnumerateFiles(wuCache))
                {
                    try { var fi = new FileInfo(file); freed += fi.Length; fi.Delete(); deleted++; }
                    catch { /* 跳过占用中的文件 */ }
                }
                AppendCleanLog($"已清理更新缓存: {deleted} 个文件，释放约 {FormatBytes(freed)}。");

                string startResult = RunCmd("net", "start wuauserv");
                AppendCleanLog(string.IsNullOrWhiteSpace(startResult) ? "✅ 服务已重新启动。" : startResult.Trim());
            });
        }

        /// <summary>批量清理若干目录下的文件，返回删除数量和释放的字节数，供各个"清理XXX"方法复用。</summary>
        private (int deleted, long freed) CleanFilesInDirs(IEnumerable<string> dirs)
        {
            int deleted = 0; long freed = 0;
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in SafeEnumerateFiles(dir))
                {
                    try { var fi = new FileInfo(file); freed += fi.Length; fi.Delete(); deleted++; }
                    catch { /* 正被占用/无权限，跳过即可 */ }
                }
            }
            return (deleted, freed);
        }

        private async Task CleanBrowserCacheAsync()
        {
            AppendCleanLog("正在清理浏览器缓存...");
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            await Task.Run(() =>
            {
                var dirs = new List<string>
                {
                    Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                    Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"),
                    Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                    Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"),
                };
                var (deleted, freed) = CleanFilesInDirs(dirs);
                AppendCleanLog(deleted > 0
                    ? $"✅ 浏览器缓存清理完成：删除 {deleted} 个文件，释放约 {FormatBytes(freed)}。"
                    : "没有找到可清理的浏览器缓存文件（可能没装Edge/Chrome，或者浏览器正在运行占用了文件，建议先关闭浏览器再试）。");
            });
        }

        private async Task CleanThumbnailCacheAsync()
        {
            AppendCleanLog("正在清理缩略图缓存...");
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string explorerDir = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
            await Task.Run(() =>
            {
                int deleted = 0; long freed = 0;
                if (Directory.Exists(explorerDir))
                {
                    foreach (var file in Directory.EnumerateFiles(explorerDir, "thumbcache_*.db")
                             .Concat(Directory.EnumerateFiles(explorerDir, "iconcache_*.db")))
                    {
                        try { var fi = new FileInfo(file); freed += fi.Length; fi.Delete(); deleted++; }
                        catch { /* 正被资源管理器占用，跳过 */ }
                    }
                }
                AppendCleanLog(deleted > 0
                    ? $"✅ 缩略图缓存清理完成：删除 {deleted} 个文件，释放约 {FormatBytes(freed)}。"
                    : "没有找到可清理的缩略图缓存文件（也可能正被资源管理器占用，重启一下资源管理器再试试）。");
            });
        }

        private async Task CleanPrefetchAsync()
        {
            AppendCleanLog("正在清理Prefetch预读取文件...");
            string prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            await Task.Run(() =>
            {
                var (deleted, freed) = CleanFilesInDirs(new[] { prefetchDir });
                AppendCleanLog(deleted > 0
                    ? $"✅ Prefetch清理完成：删除 {deleted} 个文件，释放约 {FormatBytes(freed)}。"
                    : "没有找到可清理的Prefetch文件，或者没有足够权限访问。");
            });
        }

        private async Task CleanPackageCacheAsync()
        {
            AppendCleanLog("正在清理安装包缓存...");
            string pkgCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Package Cache");
            await Task.Run(() =>
            {
                var (deleted, freed) = CleanFilesInDirs(new[] { pkgCacheDir });
                AppendCleanLog(deleted > 0
                    ? $"✅ 安装包缓存清理完成：删除 {deleted} 个文件，释放约 {FormatBytes(freed)}。"
                    : "没有找到可清理的安装包缓存文件。");
            });
        }

        /// <summary>
        /// 【新增功能】扫描C盘，列出超过300MB的大文件（只列出来，不自动删——大文件很可能是你自己的
        /// 重要资料，交给你自己判断要不要手动清理，程序不该替你做这个决定）。
        /// </summary>
        private async Task FindLargeFilesAsync()
        {
            AppendCleanLog("正在扫描C盘大文件(>300MB)，这可能需要一点时间，请稍候...");
            const long thresholdBytes = 300L * 1024 * 1024;
            await Task.Run(() =>
            {
                var results = new List<(string path, long size)>();
                try
                {
                    var dirs = new Stack<string>();
                    dirs.Push(@"C:\");
                    while (dirs.Count > 0)
                    {
                        string dir = dirs.Pop();
                        string[] subDirs;
                        try { subDirs = Directory.GetDirectories(dir); }
                        catch { continue; } // 没权限访问的目录跳过，比如部分系统保护目录

                        foreach (var sd in subDirs)
                        {
                            // 跳过几个几乎不可能有"用户大文件"、扫描起来又特别慢/容易报权限错误的系统目录
                            string name = Path.GetFileName(sd);
                            if (name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                                continue;
                            dirs.Push(sd);
                        }

                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(dir))
                            {
                                try
                                {
                                    var fi = new FileInfo(file);
                                    if (fi.Length >= thresholdBytes) results.Add((file, fi.Length));
                                }
                                catch { /* 单个文件读取失败就跳过 */ }
                            }
                        }
                        catch { /* 目录本身访问失败就跳过 */ }
                    }
                }
                catch (Exception ex) { AppendCleanLog("扫描过程中出现异常: " + ex.Message); }

                if (results.Count == 0)
                {
                    AppendCleanLog("没有找到超过300MB的大文件。");
                    return;
                }
                AppendCleanLog($"找到 {results.Count} 个超过300MB的大文件，按大小从大到小列出前30个：");
                foreach (var (path, size) in results.OrderByDescending(r => r.size).Take(30))
                {
                    AppendCleanLog($"  {FormatBytes(size),10}  {path}");
                }
                AppendCleanLog("以上文件不会自动删除，需要清理的话请自己确认后手动删除。");
            });
        }
        #endregion
    }
}
