using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
#nullable disable

namespace NeworkTool
{
    public partial class Form1
    {
        #region ====== 5.6 摄像头批量登录 ======
        /// <summary>
        /// 批量登录：对列表里选中的设备(没选就是全部)，用同一组账号密码(可再加一个备用密码)逐台尝试登录，
        /// 显示每台的协议/型号/结果。成功的设备可以双击直接进入(自动登录)预览和改设置。
        /// 账号密码只在这个窗口的内存里，关闭即丢弃。
        /// </summary>
        private void OpenCameraBatch()
        {
            var targets = new List<CameraDeviceInfo>();
            var source = listViewCamera.SelectedItems.Count > 1 ? listViewCamera.SelectedItems.Cast<ListViewItem>() : listViewCamera.Items.Cast<ListViewItem>();
            foreach (var it in source) if (it.Tag is CameraDeviceInfo d) targets.Add(d);
            if (targets.Count == 0) { MessageBox.Show("列表里还没有设备，请先扫描。", "提示"); return; }

            var f = new Form { Text = $"批量登录 - {targets.Count} 台设备", Size = new Size(900, 560), StartPosition = FormStartPosition.CenterParent, Font = fontNormal };
            var pnl = new Panel { Dock = DockStyle.Top, Height = 74 };
            pnl.Controls.Add(new Label { Text = "用户名:", Location = new Point(12, 14), AutoSize = true });
            var txtU = new TextBox { Location = new Point(62, 10), Width = 110, Text = "admin" };
            pnl.Controls.Add(txtU);
            pnl.Controls.Add(new Label { Text = "密码:", Location = new Point(185, 14), AutoSize = true });
            var txtP = new TextBox { Location = new Point(224, 10), Width = 130, UseSystemPasswordChar = true };
            pnl.Controls.Add(txtP);
            pnl.Controls.Add(new Label { Text = "备用密码(可选):", Location = new Point(366, 14), AutoSize = true });
            var txtP2 = new TextBox { Location = new Point(470, 10), Width = 130, UseSystemPasswordChar = true };
            pnl.Controls.Add(txtP2);
            var btnGo = new Button { Text = "开始批量登录", Location = new Point(615, 8), Size = new Size(110, 28) };
            var btnStopB = new Button { Text = "停止", Location = new Point(733, 8), Size = new Size(60, 28), Enabled = false };
            pnl.Controls.Add(btnGo); pnl.Controls.Add(btnStopB);
            var lblTip = new Label { Text = "每台依次试「密码」「备用密码」；成功后双击某一行可直接进入预览/改 IP。", Location = new Point(12, 46), AutoSize = true, ForeColor = Color.DimGray };
            pnl.Controls.Add(lblTip);

            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, Font = new Font("Consolas", 9.5F) };
            lv.Columns.Add("IP 地址", 130); lv.Columns.Add("识别品牌", 170); lv.Columns.Add("协议", 70); lv.Columns.Add("结果", 80); lv.Columns.Add("型号 / 说明", 380);
            f.Controls.Add(lv); f.Controls.Add(pnl);

            var rows = new Dictionary<string, ListViewItem>();
            foreach (var d in targets)
            {
                var it = new ListViewItem(d.Ip); it.SubItems.AddRange(new[] { d.Brand, "", "等待", "" }); it.Tag = d;
                lv.Items.Add(it); rows[d.Ip] = it;
            }
            var creds = new Dictionary<string, (string u, string p)>();
            CancellationTokenSource cts = null;

            lv.MouseDoubleClick += (s, e) =>
            {
                if (lv.SelectedItems.Count == 0 || !(lv.SelectedItems[0].Tag is CameraDeviceInfo d)) return;
                bool has = creds.TryGetValue(d.Ip, out var c);
                using (var dlg = has
                    ? new CameraLoginForm(d.Ip, d.Brand, d.WebPort, d.OpenPorts, c.u, c.p)
                    : new CameraLoginForm(d.Ip, d.Brand, d.WebPort, d.OpenPorts))
                    dlg.ShowDialog(f);
            };

            btnGo.Click += async (s, e) =>
            {
                string u = txtU.Text.Trim();
                var passes = new List<string> { txtP.Text };
                if (txtP2.Text.Length > 0 && txtP2.Text != txtP.Text) passes.Add(txtP2.Text);
                if (u.Length == 0) { MessageBox.Show("请输入用户名。", "提示"); return; }
                btnGo.Enabled = false; btnStopB.Enabled = true; creds.Clear();
                cts = new CancellationTokenSource();
                int ok = 0, done = 0;
                using (var sem = new SemaphoreSlim(6))
                {
                    var tasks = targets.Select(async d =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            if (cts.IsCancellationRequested) return;
                            f.Invoke(new Action(() => { rows[d.Ip].SubItems[3].Text = "登录中..."; }));
                            bool success = false; string proto = "", info = "", okPass = null;
                            foreach (var p in passes)
                            {
                                if (cts.IsCancellationRequested) break;
                                using (var dev = new CameraLoginForm(d.Ip, d.Brand, d.WebPort, d.OpenPorts))
                                {
                                    bool r = false;
                                    try { r = await dev.HeadlessLoginAsync(u, p); } catch { }
                                    if (r)
                                    {
                                        success = true; okPass = p; proto = dev.Protocol;
                                        var lines = (dev.InfoText ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                        info = string.Join(" | ", lines.Where(x => x.StartsWith("型号") || x.StartsWith("厂商") || x.StartsWith("名称")));
                                        break;
                                    }
                                }
                            }
                            if (success) { Interlocked.Increment(ref ok); lock (creds) creds[d.Ip] = (u, okPass); }
                            Interlocked.Increment(ref done);
                            f.Invoke(new Action(() =>
                            {
                                var it = rows[d.Ip];
                                it.SubItems[2].Text = proto;
                                it.SubItems[3].Text = success ? "成功" : "失败";
                                it.SubItems[4].Text = success ? info : "账号密码错误 / 协议不支持(需开启 ONVIF) / 设备未激活";
                                it.ForeColor = success ? colorAccentG : Color.Firebrick;
                                f.Text = $"批量登录 - 成功 {ok} / 已完成 {done} / 共 {targets.Count} 台";
                            }));
                        }
                        catch { }
                        finally { sem.Release(); }
                    }).ToList();
                    try { await Task.WhenAll(tasks); } catch { }
                }
                if (!f.IsDisposed) { btnGo.Enabled = true; btnStopB.Enabled = false; }
            };
            btnStopB.Click += (s, e) => { cts?.Cancel(); };
            f.FormClosing += (s, e) => { cts?.Cancel(); };
            f.ShowDialog(this);
            creds.Clear();
        }
        #endregion
    }
}
