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
    public partial class Form1 : Form
    {
        #region ====== 1. 全局配置与核心参数 ======
        private string currentIfaceName = "WLAN";
        private readonly string homeIp = "192.168.1.222", homeMask = "255.255.255.0", homeGw = "192.168.1.1", homeDns1 = "223.5.5.5", homeDns2 = "114.114.114.114";
        private readonly string workIp = "192.168.0.222", workMask = "255.255.255.0", workGw = "192.168.0.1", workDns1 = "223.5.5.5", workDns2 = "114.114.114.114";

        // 【新增】查公网IP用，共用一个 HttpClient 实例（微软官方建议不要每次 new，会耗尽连接池）
        private static readonly HttpClient publicIpHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        // 现代 UI 配色方案
        private readonly Color colorWhite = Color.FromArgb(255, 255, 255);
        private readonly Color colorPrimary = Color.FromArgb(41, 98, 255);
        private readonly Color colorPrimaryLt = Color.FromArgb(242, 246, 255);
        private readonly Color colorAccentG = Color.FromArgb(16, 124, 65);
        private readonly Color colorAccentR = Color.FromArgb(211, 47, 47);
        private readonly Color colorWarn = Color.FromArgb(237, 108, 2);
        private readonly Color textDark = Color.FromArgb(45, 52, 66);
        private readonly Color colorBorder = Color.FromArgb(218, 224, 233);
        private readonly Color colorCyan = Color.FromArgb(0, 188, 212);
        private readonly Color colorConsoleBg = Color.FromArgb(30, 30, 30);

        // 【新增】保存 OnLoad 里算出来的 DPI 缩放比例，非窗体自身 Bounds/Size 相关的
        // "纯数字"间距（比如侧边栏按钮之间的像素间隔）没法靠 Control.Scale() 自动跟着变，
        // 得自己乘这个比例才能保持跟其它已缩放控件对齐。默认 1f，对应无缩放(96 DPI)情况。
        private float uiScaleFactor = 1f;
        private Action relayoutMainShellAction;

        private bool isDarkMode = false;
        private readonly Color darkBg = Color.FromArgb(24, 26, 32);
        private readonly Color darkPanel = Color.FromArgb(36, 39, 48);
        private readonly Color darkText = Color.FromArgb(225, 228, 235);
        private readonly Color darkBorder = Color.FromArgb(70, 74, 86);
        private readonly Dictionary<Control, Color> origBack = new Dictionary<Control, Color>();
        private readonly Dictionary<Control, Color> origFore = new Dictionary<Control, Color>();

        private readonly Font fontTitle = new Font("Microsoft YaHei", 10F, FontStyle.Bold);
        private readonly Font fontNormal = new Font("Microsoft YaHei", 9F, FontStyle.Regular);
        private readonly Font fontBold = new Font("Microsoft YaHei", 9F, FontStyle.Bold);
        #endregion

        #region ====== 2. UI 顶级容器与控件声明 ======
        private Panel tabNet, tabScan, tabPing, tabPortScan, tabTraceroute, tabDhcp, tabLoop, tabTools, tabClean, tabIpConflict, tabUninstall, tabShare, tabCamera;

        // 【新增】摄像头/监控设备扫描 Tab 用到的控件
        private ListView listViewCamera;
        private ProgressBar progressBarCamera;
        private Button btnStartCameraScan, btnStopCameraScan;
        private Label lblCameraFooter;
        private CancellationTokenSource ctsCamera;

        private Panel panelDash;
        private Label lblDashTitle, lblIface, lblIP, lblStatusGw, lblStatusDns, lblStatusWan;
        private Label lblSubnet, lblMac, lblIpMode, lblLinkSpeed, lblWifiBand;
        private Label lblHwCpu, lblHwRam, lblHwDisk, lblHwGpu;
        private ListView listViewScan;
        private ProgressBar progressBarScan;
        private Button btnStartScan, btnExportScan;
        private Label lblActiveCount, lblTotalScan;
        private TextBox logTools, logClean;

        private TabControl subTabPing;
        private TabPage pageSinglePing, pageKeepPing, pageBatchPing, pageRangePing, pageTcpPing;

        private ComboBox comboSingleTarget;
        private TextBox txtSingleCount, txtSingleSize, txtSingleConsole;
        private Button btnStartSingle, btnStopSingle;
        private Label lblSingleFooter;
        private CancellationTokenSource ctsSingle;

        private ComboBox comboKeepTarget;
        private TextBox txtKeepSize, txtKeepInterval, txtKeepConsole;
        private Button btnStartKeep, btnStopKeep, btnExportKeep, btnClearKeep;
        private Label lblKeepFooter;
        private CancellationTokenSource ctsKeep;
        private int keepSent = 0, keepSuccess = 0, keepFail = 0;

        private TextBox txtBatchList;
        private TextBox txtBatchTimeout, txtBatchThreads, txtBatchInterval;
        private ComboBox comboBatchMode;
        private Button btnStartBatch, btnStopBatch, btnExportBatch, btnClearBatch;
        private ListView listViewBatch;
        private Label lblBatchFooter;
        private CancellationTokenSource ctsBatch;

        private ComboBox comboRangeAdapter, comboRangeSubnet;
        private TextBox txtRangeTimeout, txtRangeSize, txtRangeThreads;
        private Button btnStartRange, btnStopRange, btnExportRange;
        private readonly List<(string ip, bool online)> rangePingResults = new List<(string ip, bool online)>();
        private FlowLayoutPanel panelRangeGrid;
        private Label lblRangeFooter;
        private Label[] rangeGridLabels = new Label[256];
        private CancellationTokenSource ctsRange;

        private ComboBox comboTcpTarget;
        private TextBox txtTcpPort, txtTcpCount, txtTcpTimeout, txtTcpConsole;
        private Button btnStartTcp, btnStopTcp;
        private Label lblTcpFooter;
        private CancellationTokenSource ctsTcp;

        private TextBox txtPortTarget, txtPortRange, txtPortThreads;
        private Button btnStartPortScan, btnStopPortScan, btnExportPortScan;
        private ListView listViewPorts;
        private ProgressBar progressBarPorts;
        private Label lblPortFooter;
        private CancellationTokenSource ctsPortScan;

        private TextBox txtTraceTarget, txtTraceMaxHops, txtTraceTimeout;
        private Button btnStartTrace, btnStopTrace;
        private ListView listViewTrace;
        private Label lblTraceFooter;
        private CancellationTokenSource ctsTrace;

        private TextBox logDhcp;
        private Label lblDhcpStatus;
        private Button btnStartDhcp, btnStopDhcp;
        private CancellationTokenSource ctsDhcp;
        private int dhcpDetectedCount = 0;

        private TextBox logLoop;
        private Button btnStartLoop, btnStopLoop;
        private CancellationTokenSource ctsLoop;
        private CancellationTokenSource ctsIpConflict;
        private int loopReceiveCount = 0;
        private bool isLoopTesting = false;
        private const int LoopTestPort = 59999;
        private const string LoopToken = "NET_TOOL_LOOP_TEST_2026_TOKEN";
        #endregion

        #region ====== 3. 底层 P/Invoke 导入 ======
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint physicalAddrLen);

        // 【第二批新增】清空回收站用：调用 shell32 原生 API，比自己拼 PowerShell 命令稳
        [DllImport("Shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);
        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;
        #endregion

        // 【修复】.NET Core/5+ 默认不内置 GBK(936)/GB2312 这类代码页编码，运行 ipconfig/netsh/netstat
        // 这些系统命令读取中文输出时需要额外注册 System.Text.Encoding.CodePages 才能正确解码。
        // 之前这个注册动作只写在 Program.cs 里，一旦那边漏改或者被覆盖掉就会重新乱码。
        // 这里直接放到 Form1 的静态构造函数里兜底注册一次，不再依赖 Program.cs 是否配置对——
        // 前提是 .csproj 里已经引用了 System.Text.Encoding.CodePages 这个 NuGet 包（如果没引用，
        // 这里会安静地失败，ReadRawAndDecode 里还有 UTF8 兜底，不会导致程序崩溃）。
        static Form1()
        {
            try { Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); } catch { /* 包未安装就忽略，走UTF8兜底 */ }
        }

        public Form1()
        {
            BuildModernLayout();
            _ = RefreshNetStatusAsync();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 【终极修复】前面试了 AutoScaleMode.Dpi + PerformAutoScale() 这套 WinForms 内置的
            // "自动"缩放机制，反复调整调用时机都没能真正生效——干脆不再依赖它，改成自己动手：
            // 直接读取当前窗体所在显示器的真实DPI(this.DeviceDpi，.NET Core WinForms 提供的
            // 每显示器DPI属性)，跟设计基准96相除算出缩放倍数，然后用 Control.Scale() 强制把
            // 窗体和所有子控件按这个倍数重新缩放一遍。这是最底层、最直接的办法，不会再受
            // WinForms 内部那些不透明的自动触发时机影响。
            float scaleFactor = this.DeviceDpi / 96f;
            this.uiScaleFactor = scaleFactor; // 无论是否触发下面的 Scale()，都先记下来给侧边栏布局用
            if (Math.Abs(scaleFactor - 1.0f) > 0.01f)
            {
                this.Scale(new SizeF(scaleFactor, scaleFactor));

                // 【补丁2】Scale() 把窗体从设计尺寸放大了，但窗体左上角的 Location 没有跟着变，
                // 相当于"只往右下角撑大"，视觉上就变成整个窗口偏向了屏幕右下方，不再居中。
                // 缩放完之后按新的实际尺寸重新计算一次居中位置。
                var workArea = Screen.FromControl(this).WorkingArea;
                this.Location = new Point(
                    workArea.Left + (workArea.Width - this.Width) / 2,
                    workArea.Top + (workArea.Height - this.Height) / 2);
            }

            // 【补丁3：防止窗口比屏幕还大】前一版为了给内容留够宽度，把默认窗口宽度加到了 1260，
            // 这在 DPI 缩放~150% 的电脑上没问题，但虚拟机屏幕分辨率可能本身就没有这么宽——
            // 窗口比屏幕还大，右边一截（包括"安全退出工具箱"按钮）直接超出屏幕物理边界之外，
            // 肉眼彻底看不到、也点不到，这不是控件裁切问题，是窗口本身放不下。这里做一道
            // 硬性兜底：不管前面算出的尺寸多大，都不能超过当前屏幕可用工作区（留 40px 余量），
            // 超过了就按屏幕实际大小收缩，然后重新居中。这一段不管有没有触发 DPI 缩放都会执行。
            var wa2 = Screen.FromControl(this).WorkingArea;
            int safeW = Math.Min(this.Width, wa2.Width - 40);
            int safeH = Math.Min(this.Height, wa2.Height - 40);
            if (safeW != this.Width || safeH != this.Height)
            {
                this.Size = new Size(safeW, safeH);
            }
            this.Location = new Point(
                wa2.Left + (wa2.Width - this.Width) / 2,
                wa2.Top + (wa2.Height - this.Height) / 2);

            // 【关键补丁】Scale() 缩放完之后，Anchor 引擎并没有正确地按新尺寸重新定基准，
            // 之前只能靠用户手动最大化一次窗口来"纠正"过来。这里在 Scale() 跑完后立刻手动
            // 用真实的 ClientSize 重新摆一遍最外层容器，这样软件刚打开、停在默认大小时
            // 就已经是对的，不用再等用户去点一下最大化。
            relayoutMainShellAction?.Invoke();
            _ = RefreshHardwareInfoAsync();
        }

        #region ====== 4. 主窗体自适应构建布局 ======
        private void BuildModernLayout()
        {
            // 【终极修复配套】AutoScaleMode 改成 None，缩放完全交给上面 OnLoad 里手动做的那一次
            // Scale() 调用，避免这套内置机制再跟手动缩放打架、造成缩放两次或者互相抵消。
            this.AutoScaleMode = AutoScaleMode.None;
            // 【诊断标记】这行是特意加上去帮你确认"是不是真的在跑这份新代码"的：
            // 如果编译运行之后标题栏没有显示 [BUILD-FIX20260706] 这个后缀，
            // 说明你运行的 exe 不是用这份 Form1.cs 编译出来的（可能编译到了别的目录、
            // 或者跑的是旧的 exe），这时候不管我再怎么改代码，界面都不会有变化。
            this.Text = "网络工具箱 V1.0 [FIX-v18-颜色统一+WiFi频段稳健解析]";
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            // 上一版把 MinimumSize 降到 1000，太小了——"系统高级工具"那个4列按钮网格
            // 实际需要约 844px 宽的内容区，窗口拖到 1000 时可用内容区只剩约 762px，
            // 天然装不下，所以才会出现裁切/横向滚动条，这不是 bug，是最小宽度给得
            // 比内容本身还窄。这里按"最紧的那个页面刚好不用横向滚动"重新校准最小宽度，
            // 自由拖小的范围会比上一版小一些，但换来的是这个范围内所有页面都不需要滚动。
            Size defaultSize = new Size(1150, 700);
            Size minSize = new Size(1120, 700);
            this.Size = defaultSize;
            this.MinimumSize = minSize;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.BackColor = colorWhite;

            Panel topBar = new Panel { Location = new Point(15, 15), Size = new Size(1075, 42), BackColor = colorWhite };
            this.Controls.Add(topBar);

            Label lblAppTitle = new Label { Text = "🛠️ 网络工具箱 V1.0", Font = fontTitle, ForeColor = colorPrimary, AutoSize = true, Location = new Point(4, 10) };
            topBar.Controls.Add(lblAppTitle);

            // 【重做导航】原生 TabControl 左侧竖排标签栏，标签一多（现在12个）就会排不下、
            // 自动挤出第二列，怎么调高度都是治标不治本。这次不用它自带的标签头了，
            // 改成自己做一个两级侧边栏：分"网络诊断"、"系统维护"两大类，点类别展开/收起，
            // 点具体功能才真正切换页面——TabControl 只用来当"内容容器"，标签头整个隐藏掉。
            int sidebarWidth = 200;

            Panel sidebarPanel = new Panel
            {
                Location = new Point(15, 65),
                Size = new Size(sidebarWidth, 580),
                BackColor = colorWhite,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(sidebarPanel);

            Panel contentHost = new Panel
            {
                Location = new Point(15 + sidebarWidth + 8, 65),
                Size = new Size(1075 - sidebarWidth - 8, 580)
            };
            this.Controls.Add(contentHost);

            // 【真正的根因，这次终于挖到底了】前面几轮一直以为"窗体第一层的子控件"（topBar/
            // sidebarPanel/contentHost 这几个）用原生 Anchor 是没问题的，因为截图里"最大化"之后
            // 看起来都正常——但那其实是因为"最大化"这个动作本身会强制 WinForms 做一次全新的、
            // 正确的布局计算，把之前算错的尺寸"纠正"回来了。真正有问题的，是软件刚打开、
            // 停留在默认(未调整过)窗口大小的那个状态：这时候 OnLoad 里手动调的 this.Scale()
            // 缩放完，Anchor 引擎并没有正确地按缩放后的新尺寸重新定基准，所以 topBar/sidebarPanel/
            // contentHost 这几个用 Anchor=Right/Bottom 撑开的控件，宽高算出来是错的、偏小——
            // 这也是为什么"状态看板"打开时不正常，最大化一下又变正常，缩回默认大小后反而也正常了
            // （因为最大化那次操作已经把 Anchor 基准"掰正"了）。
            // 彻底解法：这三个最外层容器也不用 Anchor 了，改成跟其余所有 Tab 页一样，手动监听
            // this(窗体本身)的 Resize 事件，用 this.ClientSize 现算尺寸；并且在 Scale() 跑完后
            // 立刻手动调一次，保证软件刚打开、停在默认大小时就是对的，不用等用户去点一下最大化。
            void RelayoutMainShell()
            {
                int w = this.ClientSize.Width;
                int h = this.ClientSize.Height;
                if (w <= 0 || h <= 0) return;

                topBar.Width = Math.Max(200, w - topBar.Left - 15);
                sidebarPanel.Height = Math.Max(200, h - sidebarPanel.Top - 15);
                contentHost.Width = Math.Max(300, w - contentHost.Left - 15);
                contentHost.Height = Math.Max(200, h - contentHost.Top - 15);
            }
            this.Resize += (s, e) => RelayoutMainShell();
            this.relayoutMainShellAction = RelayoutMainShell;
            RelayoutMainShell();

            // 【彻底重做】不用 TabControl 了——之前"往上偏移+靠父容器裁掉"来隐藏原生标签头这个办法，
            // 在页面内部还嵌套着子标签(比如"高级Ping测试"里的单个/持续/批量/网段/TCP Ping)的情况下
            // 会互相打架，修了这里漏那里。现在每个功能页直接就是一个普通 Panel，铺满 contentHost，
            // 切换页面就是"显示这一个、隐藏其它所有"，没有"标签头"这个概念，也就没有"藏不干净"这回事。
            tabNet = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabScan = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabPing = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabPortScan = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabTraceroute = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabDhcp = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabLoop = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabTools = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabClean = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabIpConflict = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabUninstall = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabShare = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };
            tabCamera = new Panel { Dock = DockStyle.Fill, BackColor = colorWhite, Visible = false };

            contentHost.Controls.AddRange(new Control[] { tabNet, tabScan, tabPing, tabPortScan, tabTraceroute, tabDhcp, tabLoop, tabIpConflict, tabShare, tabCamera, tabTools, tabUninstall, tabClean });

            BuildTabNetLayout();
            BuildTabScanLayout();
            BuildTabPingLayout();
            BuildTabPortScanLayout();
            BuildTabTracerouteLayout();
            BuildTabDhcpLayout();
            BuildTabLoopLayout();
            BuildTabIpConflictLayout();
            BuildTabShareLayout();
            BuildTabCameraLayout();
            BuildTabToolsLayout();
            BuildTabUninstallLayout();
            BuildTabCleanLayout();

            // ---- 两级侧边栏：分组 + 展开/收起 + 高亮当前选中项 ----
            var groupDefs = new (string Name, (string Label, Panel Page)[] Items)[]
            {
                ("🌐 网络诊断", new[]
                {
                    ("状态看板与快捷切换", tabNet),
                    ("局域网主机发现", tabScan),
                    ("高级 Ping 测试", tabPing),
                    ("端口开放扫描", tabPortScan),
                    ("路由追踪溯源", tabTraceroute),
                    ("DHCP 服务器检测", tabDhcp),
                    ("局域网回路测试", tabLoop),
                    ("IP冲突检测", tabIpConflict),
                    ("网络共享管理", tabShare),
                    ("摄像头设备扫描", tabCamera),
                }),
                ("🛠️ 系统维护", new[]
                {
                    ("系统高级工具", tabTools),
                    ("软件卸载", tabUninstall),
                    ("C盘深度安全清理", tabClean),
                }),
            };

            bool[] groupExpanded = new bool[groupDefs.Length];
            groupExpanded[0] = true; // 默认展开第一组（网络诊断），符合大部分人打开就要用的场景

            var headerButtons = new Button[groupDefs.Length];
            var childButtonsPerGroup = new List<Button>[groupDefs.Length];
            Button activeChildButton = null;
            Panel activePanel = null;

            // 【修复】原来这里用两个写死的 int 常量(34/36)算每一行的间距。
            // 问题是 OnLoad 里会根据当前显示器 DPI 调用 this.Scale(scaleFactor)，
            // 把所有按钮的 Size 都跟着缩放比例放大了(Win11 常见125%/150%缩放)，
            // 但这两个常量并不会跟着变——结果就是：刚打开窗口时（Scale之前的初次布局
            // 恰好用的也是未缩放的常量，凑巧对得上）看起来正常，可一旦点击分组标题
            // 触发 RelayoutSidebar() 重新排布，用的还是这套"缩放前"的间距去摆放
            // "缩放后"的按钮，实际按钮更高、算出来的间距却更小，于是互相压盖、
            // 整体错位。改成不用固定常量，实时读取按钮自身的 Height，
            // 无论有没有DPI缩放、缩放比例是多少，都能和按钮实际尺寸对齐。
            const int rowGap = 2;
            const int groupGap = 6;
            const int rowHeight = 34;        // 仅用作按钮初始设计高度，DPI缩放会在OnLoad里统一处理
            const int headerRowHeight = 36;   // 同上；RelayoutSidebar 不再依赖这两个值

            void SelectPage(Button btn, Panel page)
            {
                if (activeChildButton != null)
                {
                    activeChildButton.BackColor = colorWhite;
                    activeChildButton.ForeColor = textDark;
                    activeChildButton.Font = fontNormal;
                }
                btn.BackColor = colorPrimaryLt;
                btn.ForeColor = colorPrimary;
                btn.Font = fontBold;
                activeChildButton = btn;

                if (activePanel != null) activePanel.Visible = false;
                page.Visible = true;
                page.BringToFront();
                activePanel = page;
            }

            void RelayoutSidebar()
            {
                // 【修复】按钮的 Height 本身已经是缩放后的真实高度了（跟着 Control.Scale() 走），
                // 但这里的间距/起始坐标是我们自己算的"纯数字"，Scale() 管不到，
                // 所以要手动乘上 uiScaleFactor，不然缩放比例越大，累积的间距误差就越明显。
                int x = (int)Math.Round(8 * uiScaleFactor);
                int gap = (int)Math.Round(rowGap * uiScaleFactor);
                int gGap = (int)Math.Round(groupGap * uiScaleFactor);
                int y = x;
                for (int g = 0; g < groupDefs.Length; g++)
                {
                    headerButtons[g].Location = new Point(x, y);
                    y += headerButtons[g].Height + gap; // 用按钮真实高度，而不是写死的数字
                    foreach (var cb in childButtonsPerGroup[g])
                    {
                        cb.Visible = groupExpanded[g];
                        if (groupExpanded[g])
                        {
                            cb.Location = new Point(x, y);
                            y += cb.Height + gap; // 同上，跟随DPI缩放后的真实高度
                        }
                    }
                    y += gGap; // 组间距
                }
            }

            for (int g = 0; g < groupDefs.Length; g++)
            {
                int gi = g; // 闭包变量捕获修正
                var groupName = groupDefs[gi].Name;
                var items = groupDefs[gi].Items;

                Button header = new Button
                {
                    Text = (groupExpanded[gi] ? "▾ " : "▸ ") + groupName,
                    Size = new Size(sidebarWidth - 20, headerRowHeight),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = colorPrimary,
                    ForeColor = colorWhite,
                    Font = fontBold,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                header.FlatAppearance.BorderSize = 0;
                headerButtons[gi] = header;
                sidebarPanel.Controls.Add(header);

                var childList = new List<Button>();
                foreach (var (label, page) in items)
                {
                    Button cb = new Button
                    {
                        Text = "    " + label,
                        Size = new Size(sidebarWidth - 20, rowHeight),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = colorWhite,
                        ForeColor = textDark,
                        Font = fontNormal,
                        TextAlign = ContentAlignment.MiddleLeft
                    };
                    cb.FlatAppearance.BorderSize = 0;
                    cb.Click += (s, e) => SelectPage(cb, page);
                    sidebarPanel.Controls.Add(cb);
                    childList.Add(cb);
                }
                childButtonsPerGroup[gi] = childList;

                header.Click += (s, e) =>
                {
                    groupExpanded[gi] = !groupExpanded[gi];
                    header.Text = (groupExpanded[gi] ? "▾ " : "▸ ") + groupName;
                    RelayoutSidebar();
                };
            }

            RelayoutSidebar();
            if (childButtonsPerGroup[0].Count > 0) SelectPage(childButtonsPerGroup[0][0], groupDefs[0].Items[0].Page);

            Button btnExit = new Button
            {
                Text = "安全退出工具箱",
                Size = new Size(160, 38),
                Font = fontBold,
                BackColor = colorPrimary,
                ForeColor = colorWhite,
                FlatStyle = FlatStyle.Flat
            };
            // 【修复】这个按钮之前同时挂了原生 Anchor=Bottom|Right 和下面这段手动 SizeChanged
            // 重新定位逻辑，两套机制互相打架——这正是前面反复排查的"Anchor 跟手动 Scale()
            // 缩放打架"那个根因在这个按钮身上的表现，之前一直没查到是因为它是在最后单独加的，
            // 不在其余几个 Tab 页的布局方法里。这里去掉 Anchor，只保留手动重新定位这一套，
            // 跟其余所有控件统一成同一套逻辑。
            void RelocateExitButton()
            {
                if (this.ClientSize.Width <= 0 || this.ClientSize.Height <= 0) return;
                btnExit.Location = new Point(this.ClientSize.Width - btnExit.Width - 30, this.ClientSize.Height - btnExit.Height - 25);
            }
            RelocateExitButton();
            btnExit.FlatAppearance.BorderSize = 0;
            btnExit.Click += (s, e) => this.Close();
            this.Controls.Add(btnExit);
            btnExit.BringToFront();

            this.SizeChanged += (s, e) => RelocateExitButton();
            this.Shown += (s, e) => RelocateExitButton();
        }

        private void TabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            TabControl tc = sender as TabControl;
            if (tc == null || e.Index < 0 || e.Index >= tc.TabCount) return;

            Graphics g = e.Graphics;
            Rectangle tabRect = tc.GetTabRect(e.Index);
            bool isSelected = tc.SelectedIndex == e.Index;

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bgBrush = new SolidBrush(isSelected ? ThemeCardLt : ThemeSurface))
            {
                g.FillRectangle(bgBrush, tabRect);
            }

            if (isSelected)
            {
                using (SolidBrush indicatorBrush = new SolidBrush(colorPrimary))
                {
                    g.FillRectangle(indicatorBrush, tabRect.X, tabRect.Y, 5, tabRect.Height);
                }
            }
            else
            {
                using (Pen borderPen = new Pen(ThemeBorder, 1))
                {
                    g.DrawLine(borderPen, tabRect.X, tabRect.Bottom - 1, tabRect.Right, tabRect.Bottom - 1);
                }
            }

            string text = tc.TabPages[e.Index].Text;
            using (SolidBrush textBrush = new SolidBrush(isSelected ? colorPrimary : ThemeText))
            {
                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center
                };

                Rectangle textRect = new Rectangle(tabRect.X + 16, tabRect.Y, tabRect.Width - 16, tabRect.Height);
                g.DrawString(text, fontBold, textBrush, textRect, sf);
            }
        }
        #endregion

        #region ====== 4.1 美化扩展基础函数 ======
        private Color ThemeSurface => isDarkMode ? darkBg : colorWhite;
        private Color ThemeCardLt => isDarkMode ? darkPanel : colorPrimaryLt;
        private Color ThemeBorder => isDarkMode ? darkBorder : colorBorder;
        private Color ThemeText => isDarkMode ? darkText : textDark;

        // 【全局修复工具】原来很多地方用 Anchor=Right/Bottom 让控件跟着窗口拉伸，
        // 但这套手动 this.Scale() 做 DPI 缩放的代码跟原生 Anchor 机制混用时，
        // 会把锚定控件的宽/高算错、撑到窗口实际可视区域之外——文字被裁、圆角缺一半、
        // 滚动条摸不到，全都是这一个根因。这两个helper 用手动监听 Resize、拿当前
        // 真实的 parent.ClientSize 现算尺寸/位置来代替 Anchor，从根上避免这个坑，
        // 所有 Tab 页要"跟着变宽/变高"的控件统一改用这两个方法。
        private void AutoStretch(Control parent, Control ctrl, bool width = true, bool height = false, int rightMargin = 15, int bottomMargin = 15)
        {
            ctrl.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            void Relayout(object s, EventArgs e)
            {
                if (parent.ClientSize.Width <= 0 || parent.ClientSize.Height <= 0) return;
                if (width) ctrl.Width = Math.Max(60, parent.ClientSize.Width - ctrl.Left - rightMargin);
                if (height) ctrl.Height = Math.Max(40, parent.ClientSize.Height - ctrl.Top - bottomMargin);
            }
            parent.Resize += Relayout;
            // 【补丁】之前只在 parent.Resize 时重算，但软件刚打开时这些 Tab 页面板大多数还是
            // 隐藏状态（只有当前选中的那个可见），Resize 事件的触发时机、跟 DPI 缩放/屏幕
            // 兜底收缩的先后顺序不完全可控，偶尔会导致刚打开时数值算得不对。这里加一道保险：
            // 每次这个 Tab 页被切换显示出来的瞬间，都强制用当时最新的真实尺寸重新算一遍，
            // 不管之前的 Resize 事件链有没有正确触发过，用户点开这个页面时看到的一定是对的。
            parent.VisibleChanged += Relayout;
            Relayout(null, EventArgs.Empty);
        }

        private void AutoStickBottom(Control parent, Control ctrl, int bottomMargin = 15)
        {
            ctrl.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            void Relayout(object s, EventArgs e)
            {
                if (parent.ClientSize.Height <= 0) return;
                ctrl.Top = Math.Max(0, parent.ClientSize.Height - ctrl.Height - bottomMargin);
            }
            parent.Resize += Relayout;
            parent.VisibleChanged += Relayout;
            Relayout(null, EventArgs.Empty);
        }

        // 之前每列宽度都是写死的像素数，控件变宽之后列不会自动跟着变，右边就空出一大片白，
        // 容易让人以为"滚动条不见了/滚不动"。这里统一让 ListView 变宽时，最后一列自动补满剩余宽度。
        private void AutoFillLastColumn(ListView lv, int minWidth = 120)
        {
            lv.Resize += (s, e) => {
                if (lv.Columns.Count == 0) return;
                int used = 0;
                for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
                int remain = lv.ClientSize.Width - used - SystemInformation.VerticalScrollBarWidth - 4;
                lv.Columns[lv.Columns.Count - 1].Width = Math.Max(minWidth, remain);
            };
        }

        private GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void Stylize(Panel p, int radius = 12, bool shadow = true)
        {
            p.BorderStyle = BorderStyle.None;
            void ApplyRegion()
            {
                if (p.Width <= 0 || p.Height <= 0) return;
                p.Region = new Region(RoundedRect(new Rectangle(0, 0, p.Width, p.Height), radius));
            }
            ApplyRegion();
            p.Resize += (s, e) => { ApplyRegion(); p.Parent?.Invalidate(true); };

            p.Paint += (s, e) =>
            {
                foreach (Control child in p.Controls)
                {
                    if (child is Label lbl && lbl.BackColor != p.BackColor) lbl.BackColor = p.BackColor;
                }
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(ThemeBorder, 1))
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, p.Width - 1, p.Height - 1), radius))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            };

            if (shadow && p.Parent != null)
            {
                p.Parent.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = RoundedRect(new Rectangle(p.Left + 4, p.Top + 4, p.Width, p.Height), radius))
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(isDarkMode ? 90 : 35, 0, 0, 0)))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                };
                p.LocationChanged += (s, e) => p.Parent?.Invalidate(true);
            }
        }

        private Button CreateStyledButton(string text, int x, int y, int width, int height, bool primary = true)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = fontBold,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? colorPrimary : colorWhite,
                ForeColor = primary ? colorWhite : textDark
            };
            if (!primary) btn.FlatAppearance.BorderColor = colorBorder;
            else btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
        #endregion
    }
}
