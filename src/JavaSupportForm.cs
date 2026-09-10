using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InputBeacon
{
    internal sealed class JavaSupportForm : Form
    {
        internal JavaSupportForm()
        {
            Text = "IDEA / Java 光标支持";
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(446, 286);
            BackColor = Color.FromArgb(248, 249, 251);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            var explanation = new Label { Location = new Point(22, 18), Size = new Size(402, 78), Text = "IDEA 的 Java 编辑区需要 Java Access Bridge 提供光标位置。\n此按钮启用当前用户的 Java 辅助接口，不读取代码内容。\n启用后，请保存工作并重新打开 IDEA。" };
            var caption = new Label { Text = "检测到的 Java 运行环境", AutoSize = true, Location = new Point(22, 101) };
            string runtime = FindRuntime();
            var path = new TextBox { ReadOnly = true, Text = runtime ?? "请先打开 IDEA，再重新进入此设置", Location = new Point(22, 126), Size = new Size(402, 27) };
            var status = new Label { Text = "若启用后仍无提示，可在 IDEA 的设置 → 外观中开启\n“支持屏幕阅读器 / Support screen readers”。", ForeColor = Color.DimGray, Location = new Point(22, 166), Size = new Size(402, 54) };
            var enable = new Button { Text = "启用 Java 光标支持", Enabled = runtime != null, Location = new Point(22, 236), Size = new Size(162, 31) };
            var close = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, Location = new Point(340, 236), Size = new Size(84, 31) };
            enable.Click += async delegate
            {
                enable.Enabled = false;
                status.Text = "正在启用 Java 辅助接口…";
                try
                {
                    int code = await Task.Run(delegate
                    {
                        using (Process process = Process.Start(new ProcessStartInfo(Path.Combine(runtime, "jabswitch.exe"), "-enable") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                        {
                            if (!process.WaitForExit(10000)) return -1;
                            return process.ExitCode;
                        }
                    });
                    if (!IsDisposed) status.Text = code == 0 ? "已启用。请保存工作并重新打开 IDEA，\n然后在编辑区移动光标验证跟随。" : "启用未完成，请检查 Java 运行环境后重试。";
                }
                catch (Exception error) { if (!IsDisposed) status.Text = "无法启用：" + error.Message; }
                finally { if (!IsDisposed) enable.Enabled = true; }
            };
            Controls.AddRange(new Control[] { explanation, caption, path, status, enable, close });
            CancelButton = close;
        }

        private static string FindRuntime()
        {
            if (JavaCaret.LastRuntime != null && File.Exists(Path.Combine(JavaCaret.LastRuntime, "jabswitch.exe"))) return JavaCaret.LastRuntime;
            foreach (string name in new[] { "idea64", "idea", "pycharm64", "webstorm64", "clion64", "rider64", "goland64", "datagrip64", "javaw", "java" })
                foreach (Process process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        string runtime = JavaCaret.FindRuntime((uint)process.Id);
                        if (runtime != null && File.Exists(Path.Combine(runtime, "jabswitch.exe"))) return runtime;
                    }
            return null;
        }
    }
}
