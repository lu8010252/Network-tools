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
            this.Text = "路健的网络工具箱V1.0 [FIX-v18-颜色统一+WiFi频段稳健解析]";
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

            Label lblAppTitle = new Label { Text = "🛠️ 路健的网络工具箱V1.0", Font = fontTitle, ForeColor = colorPrimary, AutoSize = true, Location = new Point(4, 10) };
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

        #region ====== 5. 各业务选项卡面板构建与核心逻辑 ======
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
            sb.AppendLine("========== 路健的网络工具箱 · 诊断报告 ==========");
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

        private void BuildTabScanLayout()
        {
            btnStartScan = CreateStyledButton("开始 ARP 交叉精准扫描", 15, 15, 200, 35);
            progressBarScan = new ProgressBar { Location = new Point(230, 21), Size = new Size(400, 23), Style = ProgressBarStyle.Continuous };
            // 【新增】导出功能：把当前扫描结果列表直接导出成 CSV，复用了代码里已有的
            // ExportListViewToCsv 通用方法（其它几个 Tab 的导出按钮也是用它）。
            btnExportScan = CreateStyledButton("导出结果", 645, 15, 95, 35, primary: false);
            btnExportScan.Click += (s, e) => {
                if (listViewScan.Items.Count == 0)
                {
                    MessageBox.Show("当前没有可导出的扫描结果，请先执行扫描。", "提示");
                    return;
                }
                ExportListViewToCsv(listViewScan, "局域网主机发现结果");
            };
            tabScan.Controls.AddRange(new Control[] { btnStartScan, progressBarScan, btnExportScan });

            // 【修复】原来这两个标题 Label 既没设 AutoSize，也没给 Size，
            // WinForms 新建 Label 默认给的是 100x23 的固定小方框，文字必然被框死截断
            // （"底层链路层强力审计算法..."显示成"底层链路层强力"就是这个原因，跟DPI/窗口大小无关）。
            // 加上 AutoSize=true，方框跟着文字实际宽度自动撑开，不会再截断。
            Panel pDash = new Panel { Location = new Point(15, 65), Size = new Size(420, 95), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(245, 247, 250) };
            lblTotalScan = new Label { Text = "检测范围: 等待扫描...", Location = new Point(15, 38), Size = new Size(300, 20) };
            lblActiveCount = new Label { Text = "当前在线设备: 0 台", Location = new Point(15, 63), Size = new Size(300, 20), ForeColor = colorAccentG, Font = fontBold };
            pDash.Controls.AddRange(new Control[] { new Label { Text = "资产高精扫描状态", Location = new Point(10, 8), Font = fontBold, AutoSize = true }, lblTotalScan, lblActiveCount });

            // 【真正的根因】上一版给 panelPolicy 去掉了 Anchor，圆角和文字是不截断了，
            // 但代价是不再跟着窗口变宽——这只是绕开了症状，没有解决病根。
            // 真正的病根是：这套手动 this.Scale() 做 DPI 缩放的逻辑，跟 WinForms 原生
            // Anchor=Right/Bottom（锚定右/下边自动拉伸）机制混用时，会导致锚定控件的
            // 宽/高被错误地越滚越大，一路撑到窗口实际可视区域之外——文字、圆角、
            // 甚至 listViewScan 的滚动条，只要用了 Anchor=Right，都可能被顶到看不见的地方，
            // 这也是"拉不到底"的真正原因。
            // 彻底修法：这个 Tab 里凡是要跟着窗口变化尺寸的控件，一律不用 Anchor，
            // 改成监听 tabScan 的 Resize 事件、用当前真实宽高现算，不再依赖那套
            // 会跟自定义 DPI 缩放打架的锚定机制——这样无论 Scale() 怎么折腾，
            // 尺寸都是按当前真实窗口现算出来的，不会再跑出可视区域。
            Panel panelPolicy = new Panel { Location = new Point(450, 65), Size = new Size(445, 95), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(242, 250, 244) };
            Label lblPolicyContext = new Label { Text = "底层物理层特种 ARP 广播主动审计 + 经典 ICMP 双核交叉扫描。无缝穿透并精准识别开启了防火墙深度隐身的设备。", Location = new Point(12, 35), Size = new Size(420, 55), ForeColor = Color.DimGray, Font = fontNormal };
            panelPolicy.Controls.AddRange(new Control[] { new Label { Text = "底层链路层强力审计算法 (高准度修复版)", Location = Point.Empty, Font = fontBold, ForeColor = colorAccentG, AutoSize = true }, lblPolicyContext });
            tabScan.Controls.AddRange(new Control[] { pDash, panelPolicy });
            Stylize(pDash, 14);
            Stylize(panelPolicy, 14);
            // panelPolicy 尺寸变化时（下面 tabScan.Resize 里手动赋值触发），让内部文字框跟着变宽，
            // 同时 Stylize() 内部已经订阅了 panelPolicy.Resize 来重算圆角区域，两者都会正确触发。
            panelPolicy.Resize += (s, e) => { lblPolicyContext.Width = Math.Max(100, panelPolicy.Width - 24); };

            listViewScan = new ListView { Location = new Point(15, 175), Size = new Size(880, 400), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Consolas", 9.5F) };
            listViewScan.Columns.AddRange(new ColumnHeader[] { new ColumnHeader { Text = "主机 IP 地址", Width = 200 }, new ColumnHeader { Text = "硬件 MAC 地址", Width = 200 }, new ColumnHeader { Text = "探测状态", Width = 240 }, new ColumnHeader { Text = "设备制造厂商 (OUI)", Width = 180 } });
            tabScan.Controls.Add(listViewScan);
            AutoFillLastColumn(listViewScan, 180);

            // 【核心修复】不再用 Anchor，改成 tabScan 尺寸变化时手动现算每个"要跟着变化"的
            // 控件的宽/高，直接用 tabScan.ClientSize 这个当下真实值，不经过 WinForms 的
            // Anchor 缓存机制，从根上避免跟 this.Scale() 打架、把控件撑出可视区域的问题。
            void RelayoutScanTab()
            {
                int w = tabScan.ClientSize.Width;
                int h = tabScan.ClientSize.Height;
                if (w <= 0 || h <= 0) return;

                btnExportScan.Left = Math.Max(btnStartScan.Right + 15, w - 15 - btnExportScan.Width);
                progressBarScan.Width = Math.Max(100, btnExportScan.Left - 15 - progressBarScan.Left);

                panelPolicy.Width = Math.Max(200, w - panelPolicy.Left - 15);

                listViewScan.Width = Math.Max(200, w - listViewScan.Left - 15);
                listViewScan.Height = Math.Max(100, h - listViewScan.Top - 15);
            }
            tabScan.Resize += (s, e) => RelayoutScanTab();
            // 构建时先手动跑一次兜底：此时 tabScan 可能还没被真正 Dock/铺满，尺寸不一定准，
            // 但没关系，后面 Dock=Fill 生效、DPI 缩放跑完，都会再次触发 Resize 重新算一遍。
            RelayoutScanTab();

            btnStartScan.Click += async (s, e) => { await StartNetworkScanAsync(); };
        }

        private async Task StartNetworkScanAsync()
        {
            btnStartScan.Enabled = false;
            listViewScan.Items.Clear();
            progressBarScan.Value = 0;

            string localIp = GetActiveLocalIp();
            if (string.IsNullOrEmpty(localIp) || localIp.StartsWith("169.254") || localIp == "未连接网络")
            {
                MessageBox.Show("未检测到局域网有效 IP，请先接入网络！", "提示");
                btnStartScan.Enabled = true;
                return;
            }

            string subnet = localIp.Substring(0, localIp.LastIndexOf('.') + 1);
            lblTotalScan.Text = $"检测范围: {subnet}1 - 254";

            int finishedCount = 0;
            var tasks = new List<Task>();

            // 【修复：滚动条滚不到底】之前的写法是 60 个线程并发探测，每测完一台在线设备
            // 就单独跨线程 this.Invoke 一次、直接 Add 进 listViewScan，外面又包了一层
            // BeginUpdate/EndUpdate（长时间关闭重绘）。这种"长时间关闭重绘 + 高频跨线程插入"
            // 的组合，是 WinForms 原生 ListView 控件的一个已知坑：数据其实都进了 Items 集合
            // （所以"当前在线设备"计数是对的），但控件内部的滚动条量程（可滚动总高度）会跟丢，
            // 表现出来就是拖到某个位置就死活拖不动，下面还有几十台设备却看不到、滚不到。
            // 
            // 彻底的解法：扫描期间只把结果收集到线程安全的字典里（不碰 UI 控件），
            // 全部 254 个地址都测完之后，只对 listViewScan 做一次性的 Clear+AddRange，
            // 从根上避免"高频跨线程插入导致滚动条失步"这个场景，不会再复现。
            var resultsBySeq = new System.Collections.Concurrent.ConcurrentDictionary<int, ListViewItem>();

            using (var semaphore = new SemaphoreSlim(60))
            {
                for (int i = 1; i <= 254; i++)
                {
                    string targetIp = subnet + i;
                    int seq = i;
                    await semaphore.WaitAsync();

                    tasks.Add(Task.Run(async () => {
                        bool isOnline = false;
                        string macAddress = "—";
                        string statusText = "离线";

                        try
                        {
                            if (IPAddress.TryParse(targetIp, out IPAddress ip))
                            {
                                uint destIp = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
                                byte[] macBuf = new byte[6];
                                uint macLen = (uint)macBuf.Length;

                                if (SendARP(destIp, 0, macBuf, ref macLen) == 0)
                                {
                                    isOnline = true;
                                    macAddress = BitConverter.ToString(macBuf);
                                    statusText = "在线 (ARP硬件链路响应)";
                                }
                                else
                                {
                                    using (Ping p = new Ping())
                                    {
                                        PingReply reply = await p.SendPingAsync(targetIp, 300);
                                        if (reply.Status != IPStatus.Success)
                                        {
                                            // 【优化】300ms超时单次探测很容易被网络抖动误伤，
                                            // 补一次重试再下"离线"结论，减少误判。
                                            reply = await p.SendPingAsync(targetIp, 300);
                                        }
                                        if (reply.Status == IPStatus.Success)
                                        {
                                            isOnline = true;
                                            statusText = $"在线 (ICMP响应延迟: {reply.RoundtripTime}ms)";
                                            macAddress = GetMacViaArp(targetIp);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            semaphore.Release();
                            if (isOnline)
                            {
                                string vendor = GetVendorName(macAddress);
                                var item = new ListViewItem(targetIp);
                                item.SubItems.AddRange(new string[] { macAddress, statusText, vendor });
                                item.ForeColor = statusText.Contains("ARP") ? colorAccentG : Color.DarkOrange;
                                resultsBySeq[seq] = item;
                            }

                            this.Invoke(new Action(() => {
                                finishedCount++;
                                progressBarScan.Value = Math.Min(100, (int)((finishedCount / 254.0) * 100));
                                lblActiveCount.Text = $"当前在线设备: {resultsBySeq.Count} 台";
                            }));
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            // 按 IP 地址数值大小（也就是探测序号）从小到大整齐排列，方便看。
            var sortedItems = resultsBySeq
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value)
                .ToArray();

            listViewScan.BeginUpdate();
            listViewScan.Items.Clear();
            listViewScan.Items.AddRange(sortedItems);
            listViewScan.EndUpdate();

            MessageBox.Show($"双核高精交叉审计完成！精准截获当前在线活跃资产共 {listViewScan.Items.Count} 台。", "扫描完成");
            btnStartScan.Enabled = true;
        }
        #endregion

        #region ====== 5.5 局域网摄像头/监控设备扫描 ======
        /// <summary>
        /// 摄像头/NVR/路由器类设备识别信息。WebPort 是双击后要打开的 Web 管理页面端口。
        /// </summary>
        private class CameraDeviceInfo
        {
            public string Ip;
            public string Brand;
            public string Mac;
            public int WebPort;
            public string OpenPorts;
            public string Basis;
        }

        /// <summary>
        /// 专门用来抓取摄像头/路由器 Web 管理页面指纹的 HttpClient：
        /// 这类设备大部分用的是自签名证书或者干脆没证书，SSL 校验默认打开的话 https 探测会直接失败，
        /// 所以这里显式关闭证书校验；超时给得比较短(2秒)，避免个别无响应的设备拖慢整体扫描进度。
        /// 跟查公网IP用的 publicIpHttpClient 分开是因为要求不一样（一个要禁用证书校验，一个不用）。
        /// </summary>
        private static readonly HttpClient cameraHttpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true,
            AllowAutoRedirect = false
        })
        { Timeout = TimeSpan.FromSeconds(2) };

        // 【品牌识别规则】关键词匹配 Web 管理页面返回的 Server 响应头 / 认证 Realm / 页面正文，
        // 命中即视为"确认"，可信度最高。海康/大华/天地伟业/TP-LINK/水星这几个品牌的设备
        // 绝大多数会在登录页或响应头里带上品牌相关的英文/中文字样，命中率比较高。
        private static readonly (string keyword, string brand)[] cameraBrandKeywords = new[]
        {
            ("hikvision", "海康威视 (Hikvision)"),
            ("海康", "海康威视 (Hikvision)"),
            ("dahua", "大华 (Dahua)"),
            ("大华", "大华 (Dahua)"),
            ("dvrdvs-webs", "大华 (Dahua)"),
            ("tp-link", "TP-LINK"),
            ("tplink", "TP-LINK"),
            ("melogin", "水星 (Mercury)"),
            ("mercurycom", "水星 (Mercury)"),
            ("水星", "水星 (Mercury)"),
            ("tvt", "天地伟业 (TVT)"),
            ("天地伟业", "天地伟业 (TVT)"),
            ("onvif", "ONVIF 通用网络摄像头"),
        };

        private void BuildTabCameraLayout()
        {
            GroupBox groupCtrl = new GroupBox { Text = " 📷 局域网摄像头 / 监控设备扫描 ", Location = new Point(15, 15), Size = new Size(880, 100), Font = fontBold, ForeColor = colorPrimary };
            tabCamera.Controls.Add(groupCtrl);
            AutoStretch(tabCamera, groupCtrl);

            Label lblDesc = new Label
            {
                Text = "自动扫描当前网段，通过 Web 管理页面指纹 + 常见私有协议端口特征，识别海康、大华、天地伟业、TP-LINK、水星等品牌的摄像头/NVR/路由器设备。扫描完成后，双击列表中的一行即可直接用浏览器打开该设备的 Web 管理页面。",
                Location = new Point(15, 22),
                Size = new Size(850, 40),
                Font = fontNormal,
                ForeColor = Color.DimGray
            };
            groupCtrl.Controls.Add(lblDesc);
            AutoStretch(groupCtrl, lblDesc);

            btnStartCameraScan = new Button { Text = "🔍 开始扫描摄像头", Location = new Point(15, 65), Size = new Size(160, 32), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite, Font = fontBold };
            btnStartCameraScan.FlatAppearance.BorderSize = 0;
            btnStopCameraScan = new Button { Text = "🔲 停止", Location = new Point(185, 65), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false, Font = fontNormal };
            btnStopCameraScan.FlatAppearance.BorderColor = colorBorder;
            progressBarCamera = new ProgressBar { Location = new Point(280, 69), Size = new Size(585, 23), Style = ProgressBarStyle.Continuous };
            groupCtrl.Controls.AddRange(new Control[] { btnStartCameraScan, btnStopCameraScan, progressBarCamera });
            AutoStretch(groupCtrl, progressBarCamera);

            listViewCamera = new ListView { Location = new Point(15, 125), Size = new Size(880, 400), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Consolas", 9.5F) };
            listViewCamera.Columns.AddRange(new ColumnHeader[]
            {
                new ColumnHeader { Text = "设备品牌", Width = 170 },
                new ColumnHeader { Text = "IP 地址", Width = 130 },
                new ColumnHeader { Text = "开放端口", Width = 110 },
                new ColumnHeader { Text = "MAC 地址", Width = 150 },
                new ColumnHeader { Text = "识别依据 / 标识信息", Width = 300 },
            });
            tabCamera.Controls.Add(listViewCamera);
            AutoFillLastColumn(listViewCamera, 200);

            lblCameraFooter = new Label { Text = "就绪 | 点击「开始扫描摄像头」定位局域网内的监控设备...", Location = new Point(15, 530), Size = new Size(880, 26), BackColor = colorPrimaryLt, TextAlign = ContentAlignment.MiddleLeft };
            tabCamera.Controls.Add(lblCameraFooter);
            AutoStretch(tabCamera, lblCameraFooter);
            AutoStickBottom(tabCamera, lblCameraFooter);
            // listViewCamera 高度要留出底部 footer 的空间，所以底边距用 footer高度+15 的间隙。
            AutoStretch(tabCamera, listViewCamera, width: true, height: true, rightMargin: 15, bottomMargin: lblCameraFooter.Height + 20);

            // 双击直接打开该设备的 Web 管理页面
            listViewCamera.MouseDoubleClick += (s, e) =>
            {
                if (listViewCamera.SelectedItems.Count == 0) return;
                if (listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) OpenCameraWebPage(info);
            };

            // 右键菜单，跟其它页面（软件卸载/共享管理）的操作习惯保持一致
            var ctxMenu = new ContextMenuStrip();
            var ctxOpenWeb = new ToolStripMenuItem("在浏览器中打开 Web 管理页面");
            var ctxCopyIp = new ToolStripMenuItem("复制 IP 地址");
            ctxMenu.Items.AddRange(new ToolStripItem[] { ctxOpenWeb, ctxCopyIp });
            listViewCamera.ContextMenuStrip = ctxMenu;
            listViewCamera.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var hit = listViewCamera.HitTest(e.Location);
                if (hit.Item != null) hit.Item.Selected = true;
            };
            ctxOpenWeb.Click += (s, e) => { if (listViewCamera.SelectedItems.Count > 0 && listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) OpenCameraWebPage(info); };
            ctxCopyIp.Click += (s, e) => { if (listViewCamera.SelectedItems.Count > 0 && listViewCamera.SelectedItems[0].Tag is CameraDeviceInfo info) { try { Clipboard.SetText(info.Ip); } catch { } } };

            btnStartCameraScan.Click += async (s, e) => { await StartCameraScanAsync(); };
            btnStopCameraScan.Click += (s, e) => { ctsCamera?.Cancel(); };
        }

        private void OpenCameraWebPage(CameraDeviceInfo info)
        {
            try
            {
                int port = info.WebPort > 0 ? info.WebPort : 80;
                string scheme = port == 443 ? "https" : "http";
                string url = (port == 80 || port == 443) ? $"{scheme}://{info.Ip}" : $"{scheme}://{info.Ip}:{port}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开 Web 管理页面失败: " + ex.Message, "提示");
            }
        }

        private async Task<bool> IsTcpPortOpenAsync(string host, int port, int timeoutMs)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask && client.Connected)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>抓取 Web 管理页面的 Server 响应头 + 认证 Realm + 前一小段正文，拼一起用来做关键词匹配。</summary>
        private async Task<string> FetchHttpBannerAsync(string ip, int port)
        {
            string scheme = port == 443 ? "https" : "http";
            string url = (port == 80 || port == 443) ? $"{scheme}://{ip}/" : $"{scheme}://{ip}:{port}/";
            try
            {
                using (var resp = await cameraHttpClient.GetAsync(url))
                {
                    var sb = new StringBuilder();
                    if (resp.Headers.Server != null) sb.Append(resp.Headers.Server).Append(' ');
                    if (resp.Headers.WwwAuthenticate != null) sb.Append(string.Join(" ", resp.Headers.WwwAuthenticate)).Append(' ');
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(body)) sb.Append(body.Length > 1000 ? body.Substring(0, 1000) : body);
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        private string DetectCameraBrandFromBanner(string banner)
        {
            if (string.IsNullOrWhiteSpace(banner)) return null;
            string lower = banner.ToLowerInvariant();
            foreach (var (keyword, brand) in cameraBrandKeywords)
            {
                if (lower.Contains(keyword)) return brand;
            }
            return null;
        }

        private string TrimBannerForDisplay(string banner)
        {
            if (string.IsNullOrWhiteSpace(banner)) return "";
            string oneLine = Regex.Replace(banner, @"\s+", " ").Trim();
            return oneLine.Length > 140 ? oneLine.Substring(0, 140) + "..." : oneLine;
        }

        private async Task StartCameraScanAsync()
        {
            btnStartCameraScan.Enabled = false; btnStopCameraScan.Enabled = true;
            listViewCamera.Items.Clear();
            progressBarCamera.Value = 0;
            ctsCamera = new CancellationTokenSource();

            string localIp = GetActiveLocalIp();
            if (string.IsNullOrEmpty(localIp) || localIp.StartsWith("169.254") || localIp == "未连接网络")
            {
                MessageBox.Show("未检测到局域网有效 IP，请先接入网络！", "提示");
                btnStartCameraScan.Enabled = true; btnStopCameraScan.Enabled = false;
                return;
            }

            string subnet = localIp.Substring(0, localIp.LastIndexOf('.') + 1);
            lblCameraFooter.Text = $"正在扫描网段 {subnet}1 - 254 ，识别摄像头/监控设备...";

            int finished = 0; int found = 0;
            var tasks = new List<Task>();
            listViewCamera.BeginUpdate();

            // 【常见品牌端口特征】80/8080/443 是几乎所有品牌都会开放的 Web 管理页面端口；
            // 8000 是海康 SDK 私有端口、37777 是大华私有协议端口、34567 是天地伟业/众多贴牌
            // (Xiongmai系OEM机型)常用的 DVRIP 私有协议端口——这三个不是 HTTP，不拿来抓网页，
            // 只当作品牌"命中线索"，配合 Web 指纹关键词一起判断。
            int[] probePorts = { 80, 8080, 443, 8000, 37777, 34567 };

            using (var sem = new SemaphoreSlim(50))
            {
                for (int i = 1; i <= 254; i++)
                {
                    if (ctsCamera.Token.IsCancellationRequested) break;
                    string targetIp = subnet + i;
                    await sem.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (ctsCamera.Token.IsCancellationRequested) return;

                            var openFlags = await Task.WhenAll(probePorts.Select(p => IsTcpPortOpenAsync(targetIp, p, 400)));
                            var openPorts = new List<int>();
                            for (int k = 0; k < probePorts.Length; k++) if (openFlags[k]) openPorts.Add(probePorts[k]);
                            if (openPorts.Count == 0) return; // 这台主机相关端口全都没开，直接跳过，不是候选设备

                            int webPort = openPorts.Contains(80) ? 80 : (openPorts.Contains(8080) ? 8080 : (openPorts.Contains(443) ? 443 : -1));

                            string banner = webPort > 0 ? await FetchHttpBannerAsync(targetIp, webPort) : "";
                            string brand = DetectCameraBrandFromBanner(banner);
                            bool confirmed = brand != null;
                            string basis;

                            if (!confirmed)
                            {
                                if (openPorts.Contains(8000)) { brand = "疑似海康威视 (Hikvision)"; basis = "端口特征：8000(SDK私有端口)开放，未匹配到Web指纹关键词，仅供参考"; }
                                else if (openPorts.Contains(37777)) { brand = "疑似大华 (Dahua)"; basis = "端口特征：37777(私有协议端口)开放，未匹配到Web指纹关键词，仅供参考"; }
                                else if (openPorts.Contains(34567)) { brand = "疑似天地伟业 / OEM设备"; basis = "端口特征：34567(DVRIP私有协议端口)开放，未匹配到Web指纹关键词，仅供参考"; }
                                else return; // 只开了普通web端口、没有任何品牌线索的，不是本次要找的目标，跳过（避免把打印机/NAS/路由器管理页也当成摄像头列出来）
                            }
                            else
                            {
                                basis = "Web管理页面指纹匹配：" + TrimBannerForDisplay(banner);
                            }

                            string mac = GetMacViaArp(targetIp);

                            var info = new CameraDeviceInfo
                            {
                                Ip = targetIp,
                                Brand = brand,
                                Mac = mac,
                                WebPort = webPort > 0 ? webPort : 80,
                                OpenPorts = string.Join(",", openPorts),
                                Basis = basis
                            };

                            Interlocked.Increment(ref found);
                            this.Invoke(new Action(() =>
                            {
                                var item = new ListViewItem(info.Brand);
                                item.SubItems.AddRange(new[] { info.Ip, info.OpenPorts, info.Mac, info.Basis });
                                item.Tag = info;
                                item.ForeColor = confirmed ? colorAccentG : colorWarn;
                                listViewCamera.Items.Add(item);
                            }));
                        }
                        catch { }
                        finally
                        {
                            sem.Release();
                            Interlocked.Increment(ref finished);
                            this.Invoke(new Action(() =>
                            {
                                progressBarCamera.Value = Math.Min(100, (int)(finished / 254.0 * 100));
                                lblCameraFooter.Text = $"进度: {finished}/254 | 已发现疑似摄像头/监控设备: {found} 台";
                            }));
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            listViewCamera.EndUpdate();
            btnStartCameraScan.Enabled = true; btnStopCameraScan.Enabled = false;
            if (!ctsCamera.Token.IsCancellationRequested)
                MessageBox.Show($"摄像头/监控设备扫描完成！共发现 {found} 台相关设备。双击列表中的一行即可用浏览器打开其 Web 管理页面。", "扫描完成");
            else
                lblCameraFooter.Text = $"已手动停止 | 已发现疑似摄像头/监控设备: {found} 台";
        }
        #endregion

        #region ====== 6. Tab 3 高级五维 Ping 核心套件 ======
        private void BuildTabPingLayout()
        {
            subTabPing = new TabControl
            {
                Font = fontBold,
                Dock = DockStyle.Fill
            };
            tabPing.Controls.Add(subTabPing);

            pageSinglePing = new TabPage(" 📍 单个Ping ") { BackColor = colorWhite };
            pageKeepPing = new TabPage(" 🔄 持续Ping ") { BackColor = colorWhite };
            pageBatchPing = new TabPage(" 📦 批量Ping ") { BackColor = colorWhite };
            pageRangePing = new TabPage(" 🌐 网段Ping ") { BackColor = colorWhite };
            pageTcpPing = new TabPage(" ⚡ TCP Ping ") { BackColor = colorWhite };

            subTabPing.TabPages.AddRange(new TabPage[] { pageSinglePing, pageKeepPing, pageBatchPing, pageRangePing, pageTcpPing });

            BuildSinglePingModule();
            BuildKeepPingModule();
            BuildBatchPingModule();
            BuildRangePingModule();
            BuildTcpPingModule();
        }

        private void BuildSinglePingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " ⚙️ Ping参数配置 ", Location = new Point(15, 15), Size = new Size(850, 120), ForeColor = colorCyan, Font = fontBold };
            pageSinglePing.Controls.Add(groupConfig);
            AutoStretch(pageSinglePing, groupConfig);

            Label lblTarget = new Label { Text = "目标主机:", Location = new Point(15, 32), Size = new Size(70, 20), ForeColor = textDark, Font = fontNormal };
            comboSingleTarget = new ComboBox { Location = new Point(90, 28), Size = new Size(250, 25), Font = fontNormal };
            comboSingleTarget.Items.AddRange(new string[] { "223.5.5.5", "114.114.114.114", "8.8.8.8", "www.baidu.com" });
            comboSingleTarget.Text = "www.baidu.com";

            Label lblCount = new Label { Text = "测试次数:", Location = new Point(370, 32), Size = new Size(70, 20), ForeColor = textDark, Font = fontNormal };
            txtSingleCount = new TextBox { Text = "4", Location = new Point(445, 28), Size = new Size(80, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            Label lblSize = new Label { Text = "数据包大小:", Location = new Point(15, 75), Size = new Size(80, 20), ForeColor = textDark, Font = fontNormal };
            txtSingleSize = new TextBox { Text = "64", Location = new Point(95, 71), Size = new Size(70, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblUnit = new Label { Text = "字节", Location = new Point(170, 75), Size = new Size(35, 20), ForeColor = Color.Gray, Font = fontNormal };

            groupConfig.Controls.AddRange(new Control[] { lblTarget, comboSingleTarget, lblCount, txtSingleCount, lblSize, txtSingleSize, lblUnit });

            string[] sizeLabels = { "32B", "1KB", "4KB", "8KB" };
            int[] sizeValues = { 32, 1024, 4096, 8192 };
            for (int i = 0; i < sizeLabels.Length; i++)
            {
                Button btnSize = new Button { Text = sizeLabels[i], Location = new Point(220 + (i * 65), 70), Size = new Size(60, 28), FlatStyle = FlatStyle.Flat, Font = fontNormal, BackColor = colorWhite, ForeColor = textDark };
                btnSize.FlatAppearance.BorderColor = colorBorder;
                int val = sizeValues[i];
                btnSize.Click += (s, e) => txtSingleSize.Text = val.ToString();
                groupConfig.Controls.Add(btnSize);
            }

            btnStartSingle = new Button { Text = "🚀 开始Ping", Location = new Point(500, 68), Size = new Size(110, 32), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite, Font = fontBold };
            btnStartSingle.FlatAppearance.BorderSize = 0;
            btnStopSingle = new Button { Text = "🔲 停止", Location = new Point(620, 68), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopSingle.FlatAppearance.BorderColor = colorBorder;
            groupConfig.Controls.AddRange(new Control[] { btnStartSingle, btnStopSingle });

            GroupBox groupResult = new GroupBox { Text = " 📊 Ping测试结果 ", Location = new Point(15, 145), Size = new Size(850, 330), ForeColor = colorCyan, Font = fontBold };
            pageSinglePing.Controls.Add(groupResult);

            Panel cyanBar = new Panel { Location = new Point(10, 22), Size = new Size(830, 6), BackColor = colorCyan };
            txtSingleConsole = new TextBox { Location = new Point(10, 28), Size = new Size(830, 290), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 10F), BackColor = colorWhite, ForeColor = textDark };
            groupResult.Controls.AddRange(new Control[] { cyanBar, txtSingleConsole });
            AutoStretch(groupResult, cyanBar);
            AutoStretch(groupResult, txtSingleConsole, width: true, height: true, rightMargin: 10, bottomMargin: 10);

            lblSingleFooter = new Label { Text = " 就绪 | 已发送: 0 | 接收: 0 | 丢失: 0 | 丢包率: 0%", Location = new Point(15, 485), Size = new Size(850, 28), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageSinglePing.Controls.Add(lblSingleFooter);
            AutoStretch(pageSinglePing, lblSingleFooter);
            AutoStickBottom(pageSinglePing, lblSingleFooter);
            AutoStretch(pageSinglePing, groupResult, width: true, height: true, rightMargin: 15, bottomMargin: lblSingleFooter.Height + 20);

            btnStartSingle.Click += async (s, e) => { await RunSinglePingAsync(); };
            btnStopSingle.Click += (s, e) => { ctsSingle?.Cancel(); };
        }

        private async Task RunSinglePingAsync()
        {
            string target = comboSingleTarget.Text.Trim();
            if (!int.TryParse(txtSingleCount.Text, out int count) || count <= 0) return;
            if (!int.TryParse(txtSingleSize.Text, out int size) || size <= 0 || size > 65000) return;

            btnStartSingle.Enabled = false; btnStopSingle.Enabled = true;
            txtSingleConsole.Clear();
            txtSingleConsole.AppendText($"正在 Ping {target} 具有 {size} 字节的数据:\r\n\r\n");

            ctsSingle = new CancellationTokenSource();
            int sent = 0, received = 0, lost = 0;
            byte[] buf = new byte[size];

            for (int i = 0; i < count; i++)
            {
                if (ctsSingle.Token.IsCancellationRequested) { txtSingleConsole.AppendText("\r\n⚠️ 用户终止。"); break; }
                sent++;
                lblSingleFooter.Text = $" 正在测试 | 已发送: {sent} | 接收: {received} | 丢失: {lost} | 丢包率: {(int)((double)lost / sent * 100)}%";
                try
                {
                    using (Ping p = new Ping())
                    {
                        PingReply reply = await p.SendPingAsync(target, 1000, buf, new PingOptions(64, true));
                        if (reply.Status == IPStatus.Success)
                        {
                            received++;
                            // 【修复】TTL(Options.Ttl) 是 IPv4 概念，Ping 到 IPv6 地址(比如 www.baidu.com 双栈解析出的 IPv6)
                            // 时 reply.Options 本身就是 null，之前直接格式化会显示成空白的"TTL="。这里做个兜底文案。
                            string ttlText = reply.Options != null ? reply.Options.Ttl.ToString() : "N/A(IPv6无此字段)";
                            txtSingleConsole.AppendText($"来自 {reply.Address} 的回复: 字节={size} 时间={reply.RoundtripTime}ms TTL={ttlText}\r\n");
                        }
                        else
                        {
                            lost++; txtSingleConsole.AppendText($"请求超时。\r\n");
                        }
                    }
                }
                catch { lost++; txtSingleConsole.AppendText($"请求异常中断。\r\n"); }
                try { await Task.Delay(1000, ctsSingle.Token); } catch { break; }
            }
            btnStartSingle.Enabled = true; btnStopSingle.Enabled = false;
            double loss = sent > 0 ? (double)lost / sent * 100 : 0;
            lblSingleFooter.Text = $" 诊断完成 | 已发送: {sent} | 接收: {received} | 丢失: {lost} | 丢包率: {(int)loss}%";
            txtSingleConsole.AppendText($"\r\n📊 {target} 的 Ping 统计信息:\r\n    数据包: 已发送 = {sent}，已接收 = {received}，丢失 = {lost} ({(int)loss}% 丢失)\r\n");
        }

        private void BuildKeepPingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " 🔄 持续Ping配置 ", Location = new Point(15, 15), Size = new Size(850, 120), ForeColor = colorCyan, Font = fontBold };
            pageKeepPing.Controls.Add(groupConfig);
            AutoStretch(pageKeepPing, groupConfig);

            Label lblTarget = new Label { Text = "目标主机:", Location = new Point(15, 32), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            comboKeepTarget = new ComboBox { Location = new Point(90, 28), Size = new Size(460, 25), Font = fontNormal };
            comboKeepTarget.Items.AddRange(new string[] { "223.5.5.5", "114.114.114.114", "www.baidu.com" });
            comboKeepTarget.Text = "223.5.5.5";

            Label lblSize = new Label { Text = "数据包大小:", Location = new Point(570, 32), Size = new Size(80, 20), Font = fontNormal, ForeColor = textDark };
            txtKeepSize = new TextBox { Text = "32", Location = new Point(655, 28), Size = new Size(80, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            Label lblInterval = new Label { Text = "间隔时间:", Location = new Point(15, 75), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            txtKeepInterval = new TextBox { Text = "1", Location = new Point(90, 71), Size = new Size(60, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblSec = new Label { Text = "秒", Location = new Point(155, 75), Size = new Size(25, 20), Font = fontNormal, ForeColor = Color.Gray };

            btnStartKeep = new Button { Text = "🔄 开始持续Ping", Location = new Point(200, 68), Size = new Size(130, 32), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite };
            btnStopKeep = new Button { Text = "🔲 停止", Location = new Point(340, 68), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnStopKeep.FlatAppearance.BorderColor = colorBorder; btnStopKeep.Enabled = false;
            btnExportKeep = new Button { Text = "💾 导出结果", Location = new Point(430, 68), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnExportKeep.FlatAppearance.BorderColor = colorBorder;
            btnClearKeep = new Button { Text = "🧹 清空结果", Location = new Point(530, 68), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnClearKeep.FlatAppearance.BorderColor = colorBorder;

            groupConfig.Controls.AddRange(new Control[] { lblTarget, comboKeepTarget, lblSize, txtKeepSize, lblInterval, txtKeepInterval, lblSec, btnStartKeep, btnStopKeep, btnExportKeep, btnClearKeep });

            GroupBox groupResult = new GroupBox { Text = " 🔄 持续Ping结果 ", Location = new Point(15, 145), Size = new Size(850, 330), ForeColor = colorCyan, Font = fontBold };
            pageKeepPing.Controls.Add(groupResult);

            Panel cyanBar = new Panel { Location = new Point(10, 22), Size = new Size(830, 6), BackColor = colorCyan };
            txtKeepConsole = new TextBox { Location = new Point(10, 28), Size = new Size(830, 290), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 10F), BackColor = colorWhite, ForeColor = textDark };
            groupResult.Controls.AddRange(new Control[] { cyanBar, txtKeepConsole });
            AutoStretch(groupResult, cyanBar);
            AutoStretch(groupResult, txtKeepConsole, width: true, height: true, rightMargin: 10, bottomMargin: 10);

            lblKeepFooter = new Label { Text = "就绪 | 已发送: 0 | 成功: 0 | 失败: 0 | 成功率: 0%", Location = new Point(15, 465), Size = new Size(850, 28), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageKeepPing.Controls.Add(lblKeepFooter);
            AutoStretch(pageKeepPing, lblKeepFooter);
            AutoStickBottom(pageKeepPing, lblKeepFooter);
            AutoStretch(pageKeepPing, groupResult, width: true, height: true, rightMargin: 15, bottomMargin: lblKeepFooter.Height + 20);

            btnStartKeep.Click += async (s, e) => { await RunKeepPingAsync(); };
            btnStopKeep.Click += (s, e) => { ctsKeep?.Cancel(); };
            btnClearKeep.Click += (s, e) => { txtKeepConsole.Clear(); keepSent = keepSuccess = keepFail = 0; lblKeepFooter.Text = "就绪 | 已发送: 0 | 成功: 0 | 失败: 0 | 成功率: 0%"; };
            btnExportKeep.Click += (s, e) => { SaveConsoleLog(txtKeepConsole.Text, "持续Ping结果"); };
        }

        private async Task RunKeepPingAsync()
        {
            string target = comboKeepTarget.Text.Trim();
            if (!int.TryParse(txtKeepSize.Text, out int size) || !int.TryParse(txtKeepInterval.Text, out int interval) || interval <= 0) return;
            btnStartKeep.Enabled = false; btnStopKeep.Enabled = true;
            ctsKeep = new CancellationTokenSource();
            byte[] buf = new byte[size];

            while (!ctsKeep.Token.IsCancellationRequested)
            {
                keepSent++;
                try
                {
                    using (Ping p = new Ping())
                    {
                        PingReply reply = await p.SendPingAsync(target, 1000, buf);
                        if (reply.Status == IPStatus.Success)
                        {
                            keepSuccess++;
                            txtKeepConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] 来自 {reply.Address}: 字节={size} 时间={reply.RoundtripTime}ms\r\n");
                        }
                        else
                        {
                            keepFail++; txtKeepConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] 探测超时。\r\n");
                        }
                    }
                }
                catch { keepFail++; txtKeepConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] 链路底层不可达。\r\n"); }
                double succRate = keepSent > 0 ? (double)keepSuccess / keepSent * 100 : 0;
                lblKeepFooter.Text = $"正在测试 | 已发送: {keepSent} | 成功: {keepSuccess} | 失败: {keepFail} | 成功率: {(int)succRate}%";
                try { await Task.Delay(interval * 1000, ctsKeep.Token); } catch { break; }
            }
            btnStartKeep.Enabled = true; btnStopKeep.Enabled = false;
        }

        private void BuildBatchPingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " 📦 批量Ping设置 ", Location = new Point(15, 10), Size = new Size(850, 160), ForeColor = colorCyan, Font = fontBold };
            pageBatchPing.Controls.Add(groupConfig);
            AutoStretch(pageBatchPing, groupConfig);

            Label lblListTitle = new Label { Text = "🖨️ 主机列表 (每行一个IP/域名):", Location = new Point(15, 22), Size = new Size(200, 18), Font = fontNormal, ForeColor = textDark };
            txtBatchList = new TextBox { Location = new Point(15, 42), Size = new Size(400, 105), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9.5F), Text = "223.5.5.5\r\n8.8.8.8\r\n114.114.114.114\r\nbaidu.com" };
            groupConfig.Controls.AddRange(new Control[] { lblListTitle, txtBatchList });

            Label lblT = new Label { Text = "⏱️ 超时(ms):", Location = new Point(430, 25), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtBatchTimeout = new TextBox { Text = "1500", Location = new Point(515, 22), Size = new Size(65, 23), Font = fontNormal };
            Label lblTh = new Label { Text = "⚡ 并发/线程:", Location = new Point(600, 25), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtBatchThreads = new TextBox { Text = "100", Location = new Point(690, 22), Size = new Size(65, 23), Font = fontNormal };

            Label lblM = new Label { Text = "🔄 模式:", Location = new Point(430, 60), Size = new Size(60, 20), Font = fontNormal, ForeColor = textDark };
            comboBatchMode = new ComboBox { Location = new Point(495, 57), Size = new Size(100, 25), Font = fontNormal };
            comboBatchMode.Items.Add("快速扫描"); comboBatchMode.Text = "快速扫描";
            Label lblI = new Label { Text = "⏱️ 间隔(s):", Location = new Point(610, 60), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            txtBatchInterval = new TextBox { Text = "5", Location = new Point(680, 57), Size = new Size(50, 23), Font = fontNormal };

            groupConfig.Controls.AddRange(new Control[] { lblT, txtBatchTimeout, lblTh, txtBatchThreads, lblM, comboBatchMode, lblI, txtBatchInterval });

            btnStartBatch = new Button { Text = "🚀 开始", Location = new Point(435, 105), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite };
            btnStopBatch = new Button { Text = "🔲 停止", Location = new Point(525, 105), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopBatch.FlatAppearance.BorderColor = colorBorder;
            btnExportBatch = new Button { Text = "💾 导出", Location = new Point(615, 105), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnExportBatch.FlatAppearance.BorderColor = colorBorder;
            btnClearBatch = new Button { Text = "🧹 清空", Location = new Point(705, 105), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnClearBatch.FlatAppearance.BorderColor = colorBorder;

            groupConfig.Controls.AddRange(new Control[] { btnStartBatch, btnStopBatch, btnExportBatch, btnClearBatch });

            listViewBatch = new ListView { Location = new Point(15, 180), Size = new Size(850, 295), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Segoe UI", 9F) };
            listViewBatch.Columns.AddRange(new ColumnHeader[] {
                new ColumnHeader { Text = "#", Width = 50 }, new ColumnHeader { Text = "主机/IP", Width = 140 }, new ColumnHeader { Text = "状态", Width = 80 },
                new ColumnHeader { Text = "延迟(ms)", Width = 80 }, new ColumnHeader { Text = "最小(ms)", Width = 80 }, new ColumnHeader { Text = "最大(ms)", Width = 80 },
                new ColumnHeader { Text = "丢包率", Width = 80 }, new ColumnHeader { Text = "测试时间", Width = 150 }
            });
            pageBatchPing.Controls.Add(listViewBatch);
            AutoFillLastColumn(listViewBatch, 120);

            lblBatchFooter = new Label { Text = "就绪 | 总: 0 | 在线: 0 | 离线: 0 | 成功率: 0% | 平均延迟: -", Location = new Point(15, 485), Size = new Size(850, 28), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageBatchPing.Controls.Add(lblBatchFooter);
            AutoStretch(pageBatchPing, lblBatchFooter);
            AutoStickBottom(pageBatchPing, lblBatchFooter);
            AutoStretch(pageBatchPing, listViewBatch, width: true, height: true, rightMargin: 15, bottomMargin: lblBatchFooter.Height + 20);

            btnStartBatch.Click += async (s, e) => { await RunBatchPingAsync(); };
            btnStopBatch.Click += (s, e) => ctsBatch?.Cancel();
            btnClearBatch.Click += (s, e) => { listViewBatch.Items.Clear(); lblBatchFooter.Text = "就绪 | 总: 0 | 在线: 0 | 离线: 0 | 成功率: 0% | 平均延迟: -"; };
            btnExportBatch.Click += (s, e) => { ExportListViewToCsv(listViewBatch, "批量Ping扫描结果"); };
        }

        private async Task RunBatchPingAsync()
        {
            string[] hosts = txtBatchList.Lines;
            if (hosts.Length == 0) return;

            btnStartBatch.Enabled = false;
            btnStopBatch.Enabled = true;
            listViewBatch.Items.Clear();
            ctsBatch = new CancellationTokenSource();

            int total = 0, online = 0, offline = 0;
            int idx = 1;
            listViewBatch.BeginUpdate();
            var tasks = new List<Task>();

            foreach (var h in hosts)
            {
                if (string.IsNullOrWhiteSpace(h)) continue;
                total++;
                string currentHost = h.Trim();
                var item = new ListViewItem(idx++.ToString());
                item.SubItems.AddRange(new string[] { currentHost, "检测中...", "—", "—", "—", "0%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                listViewBatch.Items.Add(item);

                tasks.Add(Task.Run(async () => {
                    if (ctsBatch.Token.IsCancellationRequested) return;
                    try
                    {
                        using (Ping p = new Ping())
                        {
                            PingReply reply = await p.SendPingAsync(currentHost, 1500);
                            this.Invoke(new Action(() => {
                                if (reply.Status == IPStatus.Success)
                                {
                                    online++;
                                    item.SubItems[2].Text = "在线";
                                    item.SubItems[3].Text = $"{reply.RoundtripTime}";
                                    item.SubItems[4].Text = $"{reply.RoundtripTime}";
                                    item.SubItems[5].Text = $"{reply.RoundtripTime}";
                                    item.ForeColor = colorAccentG;
                                }
                                else
                                {
                                    offline++;
                                    item.SubItems[2].Text = "离线";
                                    item.ForeColor = colorAccentR;
                                }
                                lblBatchFooter.Text = $"运行中 | 总: {total} | 在线: {online} | 离线: {offline} | 成功率: {(int)((double)online / total * 100)}% | 平均延迟: -";
                            }));
                        }
                    }
                    catch
                    {
                        this.Invoke(new Action(() => {
                            offline++;
                            item.SubItems[2].Text = "离线";
                            item.ForeColor = colorAccentR;
                        }));
                    }
                }));
            }
            listViewBatch.EndUpdate();

            await Task.WhenAll(tasks);
            btnStartBatch.Enabled = true;
            btnStopBatch.Enabled = false;
        }

        /// <summary>根据网卡真实 Name 取它当前所在的 /24 网段，取不到（没连网/找不到该网卡）就返回 null。</summary>
        private string GetSubnetForIface(string ifaceName)
        {
            if (string.IsNullOrEmpty(ifaceName)) return null;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name != ifaceName) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var b = addr.Address.GetAddressBytes();
                        return $"{b[0]}.{b[1]}.{b[2]}.0/24";
                    }
                }
            }
            return null;
        }

        private void BuildRangePingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " 🌐 网段Ping参数 ", Location = new Point(15, 10), Size = new Size(850, 130), ForeColor = colorCyan, Font = fontBold };
            pageRangePing.Controls.Add(groupConfig);
            AutoStretch(pageRangePing, groupConfig);

            Label lblAd = new Label { Text = "🔌 选择网卡:", Location = new Point(15, 26), Size = new Size(80, 20), Font = fontNormal, ForeColor = textDark };
            comboRangeAdapter = new ComboBox { Location = new Point(95, 22), Size = new Size(280, 25), Font = fontNormal, DropDownStyle = ComboBoxStyle.DropDownList };

            // 【修复】原来这个下拉框只有"自动抓取主要适配器"一个选项，是摆设——
            // 既没有真正枚举本机网卡，也从没被下面的扫描逻辑读取过。现在真正枚举所有
            // 已连接、有IPv4地址的网卡，可以手动选具体某一张；选完网段会自动联动更新。
            var rangeIfaceMap = new Dictionary<string, string>(); // 下拉框显示文本 -> 网卡真实 Name
            comboRangeAdapter.Items.Add("自动检测(当前活动网卡)");
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                bool hasIPv4 = ni.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (!hasIPv4) continue;
                string display = $"{ni.Name} ({ni.Description})";
                rangeIfaceMap[display] = ni.Name;
                comboRangeAdapter.Items.Add(display);
            }
            comboRangeAdapter.SelectedIndex = 0;
            groupConfig.Controls.AddRange(new Control[] { lblAd, comboRangeAdapter });

            Label lblSn = new Label { Text = "🌐 网段:", Location = new Point(15, 60), Size = new Size(60, 20), Font = fontNormal, ForeColor = textDark };
            comboRangeSubnet = new ComboBox { Location = new Point(95, 56), Size = new Size(150, 25), Font = fontNormal };
            comboRangeSubnet.Items.AddRange(new string[] { "192.168.0.0/24", "192.168.1.0/24", "10.0.0.0/24" });

            // 【修复】默认网段不再写死成 192.168.1.0/24，改成读取当前真实活动网卡所在的网段，
            // 你现在是 192.168.0.x 就默认显示 192.168.0.0/24，不用每次手动切换。
            string autoSubnet = GetSubnetForIface(currentIfaceName) ?? "192.168.1.0/24";
            if (!comboRangeSubnet.Items.Contains(autoSubnet)) comboRangeSubnet.Items.Insert(0, autoSubnet);
            comboRangeSubnet.Text = autoSubnet;

            comboRangeAdapter.SelectedIndexChanged += (s, e) =>
            {
                string subnet = null;
                if (comboRangeAdapter.SelectedIndex == 0)
                {
                    subnet = GetSubnetForIface(currentIfaceName);
                }
                else if (rangeIfaceMap.TryGetValue(comboRangeAdapter.SelectedItem.ToString(), out string ifaceName))
                {
                    subnet = GetSubnetForIface(ifaceName);
                }
                if (subnet != null)
                {
                    if (!comboRangeSubnet.Items.Contains(subnet)) comboRangeSubnet.Items.Insert(0, subnet);
                    comboRangeSubnet.Text = subnet;
                }
            };

            Label lblHint = new Label { Text = "(动态网段自适应修复版，支持任意 standard C 类掩码拓扑)", Location = new Point(255, 60), Size = new Size(400, 20), Font = fontNormal, ForeColor = Color.Gray };
            groupConfig.Controls.AddRange(new Control[] { lblSn, comboRangeSubnet, lblHint });

            Label lblT = new Label { Text = "⏱️ 超时:", Location = new Point(15, 95), Size = new Size(55, 20), Font = fontNormal, ForeColor = textDark };
            txtRangeTimeout = new TextBox { Text = "5", Location = new Point(70, 91), Size = new Size(35, 23), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblSec = new Label { Text = "秒", Location = new Point(110, 95), Size = new Size(20, 20), Font = fontNormal, ForeColor = Color.Gray };
            Label lblSz = new Label { Text = "📦 包大小:", Location = new Point(140, 95), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            txtRangeSize = new TextBox { Text = "32", Location = new Point(210, 91), Size = new Size(40, 23), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblBy = new Label { Text = "字节", Location = new Point(255, 95), Size = new Size(35, 20), Font = fontNormal, ForeColor = Color.Gray };
            Label lblTh = new Label { Text = "⚡ 线程:", Location = new Point(300, 95), Size = new Size(55, 20), Font = fontNormal, ForeColor = textDark };
            txtRangeThreads = new TextBox { Text = "50", Location = new Point(355, 91), Size = new Size(45, 23), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            btnStartRange = new Button { Text = "🚀 开始扫描", Location = new Point(420, 88), Size = new Size(95, 30), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite };
            btnStopRange = new Button { Text = "🔲 停止", Location = new Point(525, 88), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopRange.FlatAppearance.BorderColor = colorBorder;
            btnExportRange = new Button { Text = "💾 导出", Location = new Point(605, 88), Size = new Size(70, 30), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnExportRange.FlatAppearance.BorderColor = colorBorder;

            groupConfig.Controls.AddRange(new Control[] { lblT, txtRangeTimeout, lblSec, lblSz, txtRangeSize, lblBy, lblTh, txtRangeThreads, btnStartRange, btnStopRange, btnExportRange });

            Panel panelLegend = new Panel { Location = new Point(700, 90), Size = new Size(140, 30) };
            string[] legendNames = { "在线", "离线" };
            Color[] legendColors = { colorAccentG, Color.DarkGray };
            for (int i = 0; i < 2; i++)
            {
                Label block = new Label { Location = new Point(i * 70, 6), Size = new Size(12, 12), BackColor = legendColors[i] };
                Label txt = new Label { Text = legendNames[i], Location = new Point(i * 70 + 16, 4), Size = new Size(40, 18), Font = fontNormal };
                panelLegend.Controls.AddRange(new Control[] { block, txt });
            }
            groupConfig.Controls.Add(panelLegend);
            AutoStretch(groupConfig, panelLegend);

            lblRangeFooter = new Label { Text = "就绪 | 总计: 254 | 已扫: 0 | 在线: 0 | 离线: 0 | 在线率: 0%", Location = new Point(15, 145), Size = new Size(850, 24), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageRangePing.Controls.Add(lblRangeFooter);
            AutoStretch(pageRangePing, lblRangeFooter);

            panelRangeGrid = new FlowLayoutPanel
            {
                Location = new Point(15, 175),
                Size = new Size(850, 400),
                AutoScroll = true,
                WrapContents = true,
                BorderStyle = BorderStyle.FixedSingle,
                // 【彻底修复】真正的根因找到了：FlowLayoutPanel 先按"没有滚动条"时的可用宽度
                // 计算每行能放几列，算完之后才发现内容纵向超出、需要出现竖直滚动条——
                // 但滚动条一出现就会占掉右侧 17~20px 空间，这时最后一列"本来刚好放得下"，
                // 滚动条一占位就被顶到条子底下裁掉一截。加一个跟滚动条同宽的右侧 Padding，
                // 让它在"决定每行放几列"这一步就提前把这块空间让出来，不会再出现这个问题。
                // 【再修复】底部同理：FlowLayoutPanel 计算 AutoScroll 可滚动总高度时经常会
                // 短算最后一整行，导致明明有255个方块，拖到底之前就"到底"了，最后一行(如241-255)
                // 永远看不到。这里额外加一块底部 Padding，强制滚动范围多留出至少一行的高度。
                Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth + 6, 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            pageRangePing.Controls.Add(panelRangeGrid);

            panelRangeGrid.SuspendLayout();
            Label lastLabel = null;
            // 【优化】不再包含 .255（广播地址），广播地址永远"探测得通"，扫描起来没有意义还会造成误导。
            for (int i = 1; i <= 254; i++)
            {
                // 【修复】原来 Size 写死 46px，装不下 "[ 100 ]" 这种三位数的内容会被裁切。
                // 改成 AutoSize + MinimumSize，方块宽度跟着实际文字自动撑开，不管几位数都能完整显示。
                Label lblBox = new Label
                {
                    Text = $"{i}",
                    AutoSize = true,
                    MinimumSize = new Size(44, 22),
                    Padding = new Padding(4, 3, 4, 3),
                    Margin = new Padding(3),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(240, 242, 245),
                    ForeColor = textDark,
                    Font = new Font("Consolas", 8.5F, FontStyle.Bold),
                    BorderStyle = BorderStyle.FixedSingle
                };
                rangeGridLabels[i] = lblBox;
                panelRangeGrid.Controls.Add(lblBox);
                lastLabel = lblBox;
            }

            // 【真正修复】上一版加的占位控件只有 1px 宽，在自动换行容器里会直接被塞进
            // 最后一行的行尾（跟第255号方块挤在同一行），根本没有另起一行、没撑出任何高度，
            // 等于白加。这里用 SetFlowBreak 强制在第255号方块后面换行，占位控件才会真正
            // 独占新的一整行，撑出实打实的额外可滚动高度，确保能拖到底看到 255。
            if (lastLabel != null) panelRangeGrid.SetFlowBreak(lastLabel, true);
            panelRangeGrid.Controls.Add(new Label { Text = "", AutoSize = false, Size = new Size(1, 60), Margin = new Padding(0) });
            panelRangeGrid.ResumeLayout();


            btnStartRange.Click += async (s, e) => { await RunRangePingAsync(); };
            btnStopRange.Click += (s, e) => ctsRange?.Cancel();

            // 【修复】这个按钮之前只是摆在界面上，从来没绑定过点击事件，点了毫无反应。
            btnExportRange.Click += (s, e) =>
            {
                if (rangePingResults.Count == 0) { MessageBox.Show("还没有扫描结果，先点\"开始扫描\"再导出。", "提示"); return; }
                try
                {
                    using (var sfd = new SaveFileDialog { Title = "导出网段Ping结果", Filter = "CSV文件|*.csv|文本文件|*.txt", FileName = $"网段Ping结果_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
                    {
                        if (sfd.ShowDialog() != DialogResult.OK) return;
                        var sb = new StringBuilder();
                        sb.AppendLine("IP地址,状态");
                        lock (rangePingResults)
                        {
                            foreach (var (ip, online) in rangePingResults)
                                sb.AppendLine($"{ip},{(online ? "在线" : "离线")}");
                        }
                        File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                        MessageBox.Show("导出成功。", "完成");
                    }
                }
                catch (Exception ex)
                {
                    // 【新增】之前导出失败是静默的，看不出原因。加个提示，方便定位到底是权限问题
                    // 还是路径问题还是别的什么。
                    MessageBox.Show("导出失败: " + ex.Message, "出错了");
                }
            };
        }

        private async Task RunRangePingAsync()
        {
            btnStartRange.Enabled = false; btnStopRange.Enabled = true;
            ctsRange = new CancellationTokenSource();

            for (int i = 1; i <= 254; i++)
            {
                rangeGridLabels[i].BackColor = Color.FromArgb(240, 242, 245);
                rangeGridLabels[i].ForeColor = textDark;
            }

            string rawText = comboRangeSubnet.Text.Split('/')[0].Trim();
            string ipBase = rawText.Substring(0, rawText.LastIndexOf('.') + 1);
            rangePingResults.Clear(); // 【修复】每次开始扫描前清空上一轮的记录，供"导出"按钮使用

            // 【优化】之前这里的Ping超时是写死的800ms，"超时"那个输入框根本没被读取过，是摆设。
            // 这次真正读取用户填的值（单位是秒，输入框里默认"5"），换算成毫秒。
            if (!double.TryParse(txtRangeTimeout.Text.Trim(), out double timeoutSec) || timeoutSec <= 0) timeoutSec = 5;
            int timeoutMs = Math.Max(200, (int)(timeoutSec * 1000));

            int scanned = 0, online = 0, offline = 0;
            var tasks = new List<Task>();
            using (var sem = new SemaphoreSlim(30))
            {
                // 【优化】不再扫 .255（广播地址）：每次都会显示"通"，是假阳性，没有实际参考价值。
                for (int i = 1; i <= 254; i++)
                {
                    if (ctsRange.Token.IsCancellationRequested) break;
                    int currentId = i;
                    string targetIp = ipBase + currentId;
                    await sem.WaitAsync();

                    tasks.Add(Task.Run(async () => {
                        bool success = false;
                        try
                        {
                            // 【优化】先用ARP硬件层探测（跟"局域网主机发现"一致的做法），能抓到
                            // 那些屏蔽了ICMP、但网络层其实在线的设备（不少打印机/IoT/安全加固过
                            // 的设备都会屏蔽ping）。ARP探测不到再退回Ping，Ping超时了再补一次重试，
                            // 避免偶尔丢一个包就被误判成"离线"。
                            if (GetMacViaArp(targetIp) != "—")
                            {
                                success = true;
                            }
                            else
                            {
                                using (Ping p = new Ping())
                                {
                                    PingReply r = await p.SendPingAsync(targetIp, timeoutMs);
                                    success = (r.Status == IPStatus.Success);
                                    if (!success)
                                    {
                                        PingReply r2 = await p.SendPingAsync(targetIp, timeoutMs);
                                        success = (r2.Status == IPStatus.Success);
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            sem.Release();
                            scanned++;
                            lock (rangePingResults) { rangePingResults.Add((targetIp, success)); }
                            this.Invoke(new Action(() => {
                                if (success)
                                {
                                    online++;
                                    rangeGridLabels[currentId].BackColor = colorAccentG;
                                    rangeGridLabels[currentId].ForeColor = colorWhite;
                                }
                                else
                                {
                                    offline++;
                                    rangeGridLabels[currentId].BackColor = Color.DarkGray;
                                    rangeGridLabels[currentId].ForeColor = colorWhite;
                                }
                                lblRangeFooter.Text = $"扫描中 | 总计: 254 | 已扫: {scanned} | 在线: {online} | 离线: {offline} | 在线率: {(int)((double)online / 254 * 100)}%";
                            }));
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }
            btnStartRange.Enabled = true; btnStopRange.Enabled = false;
        }

        private void BuildTcpPingModule()
        {
            GroupBox groupConfig = new GroupBox { Text = " ⚙️ TCP Ping参数配置 ", Location = new Point(15, 15), Size = new Size(850, 150), ForeColor = colorCyan, Font = fontBold };
            pageTcpPing.Controls.Add(groupConfig);
            AutoStretch(pageTcpPing, groupConfig);

            Label lblTarget = new Label { Text = "目标主机:", Location = new Point(15, 32), Size = new Size(70, 20), Font = fontNormal, ForeColor = textDark };
            comboTcpTarget = new ComboBox { Location = new Point(90, 28), Size = new Size(380, 25), Font = fontNormal };
            comboTcpTarget.Items.AddRange(new string[] { "www.baidu.com", "127.0.0.1" });
            comboTcpTarget.Text = "www.baidu.com";

            Label lblP = new Label { Text = "🔌 目标端口:", Location = new Point(490, 32), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtTcpPort = new TextBox { Text = "80", Location = new Point(580, 28), Size = new Size(80, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            Label lblC = new Label { Text = "🔢 测试次数:", Location = new Point(15, 75), Size = new Size(80, 20), Font = fontNormal, ForeColor = textDark };
            txtTcpCount = new TextBox { Text = "10", Location = new Point(95, 71), Size = new Size(80, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            Label lblT = new Label { Text = "⏱️ 超时时间:", Location = new Point(490, 75), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtTcpTimeout = new TextBox { Text = "3", Location = new Point(580, 71), Size = new Size(60, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            Label lblS = new Label { Text = "秒", Location = new Point(645, 75), Size = new Size(25, 20), Font = fontNormal, ForeColor = Color.Gray };

            groupConfig.Controls.AddRange(new Control[] { lblTarget, comboTcpTarget, lblP, txtTcpPort, lblC, txtTcpCount, lblT, txtTcpTimeout, lblS });

            string[] appLabels = { "HTTP:80", "HTTPS:443", "SSH:22", "RDP:3389", "MySQL:3306", "Redis:6379" };
            string[] appPorts = { "80", "443", "22", "3389", "3306", "6379" };
            for (int i = 0; i < appLabels.Length; i++)
            {
                Button btnPort = new Button { Text = appLabels[i], Location = new Point(15 + (i * 105), 110), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat, Font = fontNormal, BackColor = colorWhite, ForeColor = textDark };
                btnPort.FlatAppearance.BorderColor = colorBorder;
                string portVal = appPorts[i];
                btnPort.Click += (s, e) => txtTcpPort.Text = portVal;
                groupConfig.Controls.Add(btnPort);
            }

            btnStartTcp = new Button { Text = "🚀 开始TCP Ping", Location = new Point(660, 108), Size = new Size(150, 30), FlatStyle = FlatStyle.Flat, BackColor = colorCyan, ForeColor = colorWhite };
            btnStartTcp.FlatAppearance.BorderSize = 0;
            btnStopTcp = new Button { Text = "🔲 停止", Location = new Point(660, 68), Size = new Size(80, 30), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopTcp.FlatAppearance.BorderColor = colorBorder;

            groupConfig.Controls.AddRange(new Control[] { btnStartTcp, btnStopTcp });

            GroupBox groupResult = new BoxGroup { Text = " 📊 TCP Ping测试结果 ", Location = new Point(15, 175), Size = new Size(830, 250), ForeColor = colorCyan, Font = fontBold };
            pageTcpPing.Controls.Add(groupResult);

            Panel cyanBar = new Panel { Location = new Point(10, 22), Size = new Size(810, 6), BackColor = colorCyan };
            txtTcpConsole = new TextBox { Location = new Point(10, 28), Size = new Size(810, 210), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 10F), BackColor = colorWhite, ForeColor = textDark };
            groupResult.Controls.AddRange(new Control[] { cyanBar, txtTcpConsole });
            AutoStretch(groupResult, cyanBar);
            AutoStretch(groupResult, txtTcpConsole, width: true, height: true, rightMargin: 10, bottomMargin: 10);

            lblTcpFooter = new Label { Text = "就绪 | 已发送: 0 | 成功: 0 | 失败: 0 | 成功率: 0%", Location = new Point(15, 435), Size = new Size(830, 28), BackColor = Color.FromArgb(224, 247, 250), ForeColor = Color.FromArgb(0, 96, 100), Font = fontBold, TextAlign = ContentAlignment.MiddleLeft };
            pageTcpPing.Controls.Add(lblTcpFooter);
            AutoStretch(pageTcpPing, lblTcpFooter);
            AutoStickBottom(pageTcpPing, lblTcpFooter);
            AutoStretch(pageTcpPing, groupResult, width: true, height: true, rightMargin: 15, bottomMargin: lblTcpFooter.Height + 20);

            btnStartTcp.Click += async (s, e) => { await RunTcpPingAsync(); };
            btnStopTcp.Click += (s, e) => ctsTcp?.Cancel();
        }

        private class BoxGroup : GroupBox { }

        private async Task RunTcpPingAsync()
        {
            string host = comboTcpTarget.Text.Trim();
            if (!int.TryParse(txtTcpPort.Text, out int port) || port <= 0 || port > 65535) return;
            if (!int.TryParse(txtTcpCount.Text, out int count) || count <= 0) return;
            if (!int.TryParse(txtTcpTimeout.Text, out int timeoutSec) || timeoutSec <= 0) return;

            btnStartTcp.Enabled = false; btnStopTcp.Enabled = true;
            txtTcpConsole.Clear();
            txtTcpConsole.AppendText($"正在探测 {host} 对准端口: {port} ...\r\n\r\n");
            ctsTcp = new CancellationTokenSource();

            int sent = 0, success = 0, fail = 0;
            for (int i = 0; i < count; i++)
            {
                if (ctsTcp.Token.IsCancellationRequested) break;
                sent++;
                Stopwatch sw = Stopwatch.StartNew();
                bool ok = false;
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        var connectTask = client.ConnectAsync(host, port);
                        var delayTask = Task.Delay(timeoutSec * 1000);
                        var completedTask = await Task.WhenAny(connectTask, delayTask);
                        if (completedTask == connectTask && client.Connected) ok = true;
                    }
                }
                catch { }
                sw.Stop();

                if (ok)
                {
                    success++;
                    txtTcpConsole.AppendText($"来自 {host} 的 TCP 握手回复: 端口={port} 响应时间={sw.ElapsedMilliseconds}ms\r\n");
                }
                else
                {
                    fail++;
                    txtTcpConsole.AppendText($"对准 {host}:{port} 的探测握手失败或超时连接。\r\n");
                }

                double rate = sent > 0 ? (double)success / sent * 100 : 0;
                lblTcpFooter.Text = $"测试中 | 已发送: {sent} | 成功: {success} | 失败: {fail} | 成功率: {(int)rate}%";
                try { await Task.Delay(1000, ctsTcp.Token); } catch { break; }
            }
            btnStartTcp.Enabled = true; btnStopTcp.Enabled = false;
        }
        #endregion

        #region ====== 7. 高并发端口快速扫描器 ======
        private void BuildTabPortScanLayout()
        {
            GroupBox groupCtrl = new GroupBox { Text = " 🔍 TCP高并发端口快速扫描器 ", Location = new Point(15, 15), Size = new Size(880, 115), Font = fontBold, ForeColor = colorPrimary };
            tabPortScan.Controls.Add(groupCtrl);
            AutoStretch(tabPortScan, groupCtrl);

            Label lblTarget = new Label { Text = "目标IP/主机:", Location = new Point(15, 30), Size = new Size(85, 20), Font = fontNormal, ForeColor = textDark };
            txtPortTarget = new TextBox { Text = "127.0.0.1", Location = new Point(105, 26), Size = new Size(180, 25), Font = fontNormal };

            Label lblRange = new Label { Text = "扫描端口范围:", Location = new Point(300, 30), Size = new Size(90, 20), Font = fontNormal, ForeColor = textDark };
            txtPortRange = new TextBox { Text = "21,22,23,25,80,135,139,443,445,1433,3306,3389,8080", Location = new Point(395, 26), Size = new Size(470, 25), Font = fontNormal };
            groupCtrl.Controls.AddRange(new Control[] { lblTarget, txtPortTarget, lblRange, txtPortRange });
            AutoStretch(groupCtrl, txtPortRange);

            Label lblTh = new Label { Text = "并发线程/频率:", Location = new Point(15, 72), Size = new Size(90, 20), Font = fontNormal, ForeColor = textDark };
            txtPortThreads = new TextBox { Text = "100", Location = new Point(105, 68), Size = new Size(60, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };

            btnStartPortScan = new Button { Text = "🚀 爆破扫描", Location = new Point(190, 65), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite };
            btnStartPortScan.FlatAppearance.BorderSize = 0;
            btnStopPortScan = new Button { Text = "🔲 停止", Location = new Point(300, 65), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopPortScan.FlatAppearance.BorderColor = colorBorder;
            btnExportPortScan = new Button { Text = "💾 导出资产", Location = new Point(390, 65), Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark };
            btnExportPortScan.FlatAppearance.BorderColor = colorBorder;

            progressBarPorts = new ProgressBar { Location = new Point(495, 72), Size = new Size(370, 20), Style = ProgressBarStyle.Continuous };
            groupCtrl.Controls.AddRange(new Control[] { lblTh, txtPortThreads, btnStartPortScan, btnStopPortScan, btnExportPortScan, progressBarPorts });
            AutoStretch(groupCtrl, progressBarPorts);

            listViewPorts = new ListView { Location = new Point(15, 145), Size = new Size(880, 400), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Segoe UI", 9.5F) };
            listViewPorts.Columns.AddRange(new ColumnHeader[] { new ColumnHeader { Text = "目标端口 (Port)", Width = 120 }, new ColumnHeader { Text = "当前开放状态", Width = 180 }, new ColumnHeader { Text = "推测标准已知服务", Width = 260 }, new ColumnHeader { Text = "审计扫描时刻", Width = 200 } });
            tabPortScan.Controls.Add(listViewPorts);
            AutoFillLastColumn(listViewPorts, 180);

            lblPortFooter = new Label { Text = "就绪 | 等待开启端口深度审计...", Location = new Point(15, 555), Size = new Size(880, 26), BackColor = Color.FromArgb(242, 246, 255), TextAlign = ContentAlignment.MiddleLeft };
            tabPortScan.Controls.Add(lblPortFooter);
            AutoStretch(tabPortScan, lblPortFooter);
            AutoStickBottom(tabPortScan, lblPortFooter);
            AutoStretch(tabPortScan, listViewPorts, width: true, height: true, rightMargin: 15, bottomMargin: lblPortFooter.Height + 20);

            btnStartPortScan.Click += async (s, e) => { await RunPortScanAsync(); };
            btnStopPortScan.Click += (s, e) => { ctsPortScan?.Cancel(); };
            btnExportPortScan.Click += (s, e) => { ExportListViewToCsv(listViewPorts, "开放端口审计报表"); };
        }

        private async Task RunPortScanAsync()
        {
            string host = txtPortTarget.Text.Trim();
            if (string.IsNullOrEmpty(host)) return;

            List<int> targetPorts = new List<int>();
            try
            {
                string[] parts = txtPortRange.Text.Split(',');
                foreach (var p in parts)
                {
                    if (p.Contains("-"))
                    {
                        string[] rng = p.Split('-');
                        int start = int.Parse(rng[0]); int end = int.Parse(rng[1]);
                        for (int k = start; k <= end; k++) targetPorts.Add(k);
                    }
                    else
                    {
                        targetPorts.Add(int.Parse(p));
                    }
                }
            }
            catch { MessageBox.Show("端口范围语法格式编写有误！支持 80,443 或 21-1024 混合格式。", "规范格式提示"); return; }

            if (targetPorts.Count == 0) return;
            if (!int.TryParse(txtPortThreads.Text, out int maxThreads) || maxThreads <= 0) maxThreads = 50;

            btnStartPortScan.Enabled = false; btnStopPortScan.Enabled = true;
            listViewPorts.Items.Clear(); progressBarPorts.Value = 0;
            ctsPortScan = new CancellationTokenSource();

            int checkedCount = 0; int openedCount = 0;
            var tasks = new List<Task>();
            // 跟局域网发现页同样的修复：不再是每测完一个端口就单独跨线程 Add 一次，
            // 改成先收集到线程安全字典里，全部测完后一次性 AddRange，避免 ListView 滚动条失步。
            var resultsByPort = new System.Collections.Concurrent.ConcurrentDictionary<int, ListViewItem>();

            using (var sem = new SemaphoreSlim(maxThreads))
            {
                foreach (int port in targetPorts)
                {
                    if (ctsPortScan.Token.IsCancellationRequested) break;
                    await sem.WaitAsync();
                    int currentPort = port;

                    tasks.Add(Task.Run(async () => {
                        bool isOpen = false;
                        try
                        {
                            using (TcpClient client = new TcpClient())
                            {
                                var connectTask = client.ConnectAsync(host, currentPort);
                                if (await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask)
                                {
                                    if (client.Connected) isOpen = true;
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            sem.Release();
                            Interlocked.Increment(ref checkedCount);
                            if (isOpen)
                            {
                                Interlocked.Increment(ref openedCount);
                                string svc = GetPlaceholderService(currentPort);
                                var item = new ListViewItem(currentPort.ToString());
                                item.SubItems.AddRange(new string[] { "开放 (Open/Listening)", svc, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                                item.ForeColor = colorAccentG;
                                resultsByPort[currentPort] = item;
                            }
                            this.Invoke(new Action(() => {
                                progressBarPorts.Value = Math.Min(100, (int)((double)checkedCount / targetPorts.Count * 100));
                                lblPortFooter.Text = $" 进度: {checkedCount}/{targetPorts.Count} | 开放总数: {openedCount} 个开放端口";
                            }));
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            var sortedPortItems = resultsByPort.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
            listViewPorts.BeginUpdate();
            listViewPorts.Items.Clear();
            listViewPorts.Items.AddRange(sortedPortItems);
            listViewPorts.EndUpdate();

            btnStartPortScan.Enabled = true; btnStopPortScan.Enabled = false;
            MessageBox.Show($"开放端口深度摸底审计完成！共发现 {openedCount} 个活动通信端口。", "审计完成");
        }

        private string GetPlaceholderService(int port)
        {
            switch (port)
            {
                case 21: return "FTP 文件传输服务";
                case 22: return "SSH 安全外壳协议远程协议";
                case 23: return "Telnet 明文远程终端";
                case 25: return "SMTP 简单邮件路由协议";
                case 80: return "HTTP 基础万维网开放数据流";
                case 135: return "RPC 微软远程过程调用基础";
                case 139: return "NetBIOS 局域网共享映射基础";
                case 443: return "HTTPS 安全高阶加密网页协议";
                case 445: return "SMB 微软底层高级共享风暴缺陷";
                case 1433: return "Microsoft SQL Server 数据库";
                case 3306: return "MySQL 开放通用数据库存储";
                case 3389: return "RDP 微软官方图形远程桌面";
                case 8080: return "Apache/Tomcat 经典高阶代理容器";
                default: return "未知自建/特殊闭源私有应用流";
            }
        }
        #endregion

        #region ====== 8. 可视化全路由追踪溯源 ======
        private void BuildTabTracerouteLayout()
        {
            GroupBox groupCtrl = new GroupBox { Text = " 🧭 多中继节点路由追踪及链路耗时溯源控制台 ", Location = new Point(15, 15), Size = new Size(880, 150), Font = fontBold, ForeColor = colorPrimary };
            tabTraceroute.Controls.Add(groupCtrl);
            AutoStretch(tabTraceroute, groupCtrl);

            int gap = 8;
            int yLbl = 55, yBox = 51, yBtn = 95;

            // 【彻底修复：路由追踪按钮被裁切】之前反复调过间距，也加过"页面切换时强制重算"，
            // 但只要"参数输入框 + 两个按钮"全挤在一行，某些高 DPI 缩放倍数下这一行需要的
            // 总宽度就是会比设计给的空间更宽——不管怎么抠像素、怎么优化触发时机都治标不治本。
            // 这次不跟像素较劲了，把两个按钮挪到单独一行（输入框们一行，按钮另起一行），
            // 从结构上让这一行不可能再因为宽度不够被裁，不用再猜任何 DPI 缩放倍数。
            Label lblT = new Label { Text = "探测总目标:", AutoSize = true, Location = new Point(15, yLbl), Font = fontNormal, ForeColor = textDark };
            groupCtrl.Controls.Add(lblT);
            txtTraceTarget = new TextBox { Text = "www.baidu.com", Location = new Point(lblT.Right + gap, yBox), Size = new Size(220, 25), Font = fontNormal };
            groupCtrl.Controls.Add(txtTraceTarget);

            Label lblM = new Label { Text = "Max跳数(TTL):", AutoSize = true, Location = new Point(txtTraceTarget.Right + gap * 2, yLbl), Font = fontNormal, ForeColor = textDark };
            groupCtrl.Controls.Add(lblM);
            txtTraceMaxHops = new TextBox { Text = "30", Location = new Point(lblM.Right + gap, yBox), Size = new Size(50, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            groupCtrl.Controls.Add(txtTraceMaxHops);

            Label lblTo = new Label { Text = "阈限超时(ms):", AutoSize = true, Location = new Point(txtTraceMaxHops.Right + gap * 2, yLbl), Font = fontNormal, ForeColor = textDark };
            groupCtrl.Controls.Add(lblTo);
            txtTraceTimeout = new TextBox { Text = "1200", Location = new Point(lblTo.Right + gap, yBox), Size = new Size(60, 25), Font = fontNormal, TextAlign = HorizontalAlignment.Center };
            groupCtrl.Controls.Add(txtTraceTimeout);

            btnStartTrace = new Button { Text = "🚀 路径追踪", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 0, 14, 0), Location = new Point(15, yBtn), Height = 32, FlatStyle = FlatStyle.Flat, BackColor = colorPrimary, ForeColor = colorWhite };
            btnStartTrace.FlatAppearance.BorderSize = 0;
            groupCtrl.Controls.Add(btnStartTrace);

            btnStopTrace = new Button { Text = "🔲 中断", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 0, 14, 0), Location = new Point(btnStartTrace.Right + gap, yBtn), Height = 32, FlatStyle = FlatStyle.Flat, BackColor = colorWhite, ForeColor = textDark, Enabled = false };
            btnStopTrace.FlatAppearance.BorderColor = colorBorder;
            groupCtrl.Controls.Add(btnStopTrace);

            listViewTrace = new ListView { Location = new Point(15, 180), Size = new Size(880, 400), View = View.Details, FullRowSelect = true, GridLines = true, Scrollable = true, Font = new Font("Consolas", 9.5F) };
            listViewTrace.Columns.AddRange(new ColumnHeader[] { new ColumnHeader { Text = "跃点 (Hop)", Width = 80 }, new ColumnHeader { Text = "中间节点路由交换机 IP", Width = 240 }, new ColumnHeader { Text = "往返耗时 (RTT延迟)", Width = 200 }, new ColumnHeader { Text = "网络质量诊断结论", Width = 220 } });
            tabTraceroute.Controls.Add(listViewTrace);
            AutoFillLastColumn(listViewTrace, 180);

            lblTraceFooter = new Label { Text = "就绪 | 等待开启 TTL 衰减拓扑追踪...", Location = new Point(15, 590), Size = new Size(880, 26), BackColor = Color.FromArgb(242, 246, 255), TextAlign = ContentAlignment.MiddleLeft };
            tabTraceroute.Controls.Add(lblTraceFooter);
            AutoStretch(tabTraceroute, lblTraceFooter);
            AutoStickBottom(tabTraceroute, lblTraceFooter);
            AutoStretch(tabTraceroute, listViewTrace, width: true, height: true, rightMargin: 15, bottomMargin: lblTraceFooter.Height + 20);

            btnStartTrace.Click += async (s, e) => { await RunTracerouteAsync(); };
            btnStopTrace.Click += (s, e) => ctsTrace?.Cancel();
        }

        private async Task RunTracerouteAsync()
        {
            string target = txtTraceTarget.Text.Trim();
            if (string.IsNullOrEmpty(target)) return;
            if (!int.TryParse(txtTraceMaxHops.Text, out int maxHops) || maxHops <= 0 || maxHops > 128) maxHops = 30;
            if (!int.TryParse(txtTraceTimeout.Text, out int timeout) || timeout <= 0) timeout = 1200;

            btnStartTrace.Enabled = false; btnStopTrace.Enabled = true;
            listViewTrace.Items.Clear();
            lblTraceFooter.Text = "核心路径测绘计算展开中...";
            ctsTrace = new CancellationTokenSource();
            txtTraceTarget.Enabled = false;

            _ = Task.Run(async () => {
                try
                {
                    byte[] buffer = new byte[32];
                    using (Ping ping = new Ping())
                    {
                        for (int ttl = 1; ttl <= maxHops; ttl++)
                        {
                            if (ctsTrace.Token.IsCancellationRequested)
                            {
                                this.Invoke(new Action(() => {
                                    var it = new ListViewItem("*"); it.SubItems.AddRange(new string[] { "⚠️ 用户强行终止", "—", "手动打断" });
                                    listViewTrace.Items.Add(it);
                                }));
                                break;
                            }

                            PingOptions opt = new PingOptions(ttl, true);
                            Stopwatch sw = Stopwatch.StartNew();
                            PingReply reply = await ping.SendPingAsync(target, timeout, buffer, opt);
                            sw.Stop();

                            bool finished = false;
                            this.Invoke(new Action(() => {
                                var item = new ListViewItem(ttl.ToString());
                                if (reply.Status == IPStatus.TtlExpired)
                                {
                                    item.SubItems.AddRange(new string[] { reply.Address.ToString(), $"{sw.ElapsedMilliseconds} ms", "中继节点转发正常" });
                                    item.ForeColor = textDark;
                                }
                                else if (reply.Status == IPStatus.Success)
                                {
                                    item.SubItems.AddRange(new string[] { reply.Address.ToString(), $"{sw.ElapsedMilliseconds} ms", "🏁 已成功抵达骨干终点主机" });
                                    item.ForeColor = colorAccentG; item.Font = fontBold;
                                    finished = true;
                                }
                                else
                                {
                                    item.SubItems.AddRange(new string[] { " * * * ", "请求超时", "💣 该路由器节点严限ICMP或拒绝回执" });
                                    item.ForeColor = colorAccentR;
                                }
                                listViewTrace.Items.Add(item);
                                lblTraceFooter.Text = $"正在追踪第 {ttl} 跳节点...";
                            }));

                            if (finished) break;
                            await Task.Delay(200);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() => MessageBox.Show($"路由链路审计发生致命内部异常: {ex.Message}")));
                }
                finally
                {
                    this.Invoke(new Action(() => {
                        btnStartTrace.Enabled = true; btnStopTrace.Enabled = false;
                        txtTraceTarget.Enabled = true;
                        lblTraceFooter.Text = "路由追踪溯源工作链正常完结。";
                    }));
                }
            });
        }
        #endregion

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

        #region ====== 10. 局域网物理回路环路审计与尾部代码完美补齐 ======
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

        #region ====== 11. 其它缺失的基础依赖函数补齐 (确保顺利编译无错) ======
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