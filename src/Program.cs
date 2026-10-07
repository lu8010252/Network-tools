using System;
using System.Windows.Forms;

namespace NeworkTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 与 VS 「Windows 窗体应用」模板默认一致:系统级 DPI 感知。
            // Form1 里的缩放逻辑依赖 DeviceDpi,所以这里必须是 DPI 感知模式。
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
