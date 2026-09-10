using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InputBeacon
{
    internal static class FollowTests
    {
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        internal static void Run(Action<string, bool> check, string folder, string settingsPath)
        {
            var lifetime = new FollowLifetime();
            var state = new InputState { Mode = InputMode.English };
            lifetime.Observe(state, 3, 100);
            check("Follow appears when enabled with a caret", lifetime.ShouldShow(true, 3, true, 100));
            lifetime.Observe(state, 3, 2500);
            check("Moving caret does not extend a timed hint", !lifetime.ShouldShow(true, 3, true, 3100));
            state.Mode = InputMode.Chinese;
            lifetime.Observe(state, 3, 4000);
            check("Input mode change retriggers hint", lifetime.ShouldShow(true, 3, true, 6999));
            state.Caps = true;
            lifetime.Observe(state, 3, 6000);
            check("Caps change resets hint duration", lifetime.ShouldShow(true, 3, true, 8999) && !lifetime.ShouldShow(true, 3, true, 9000));
            state.Shift = true;
            lifetime.Observe(state, 3, 9100);
            check("Shift case change retriggers hint", lifetime.ShouldShow(true, 3, true, 10000));
            check("Always mode survives timed expiry", lifetime.ShouldShow(true, 0, true, 100000));
            check("Missing caret hides even always mode", !lifetime.ShouldShow(true, 0, false, 100000));
            check("Follow switch overrides always mode", !lifetime.ShouldShow(false, 0, true, 100000));
            lifetime.Reset();
            lifetime.Observe(state, 3, 200000);
            check("Enabling follow starts a fresh hint", lifetime.ShouldShow(true, 3, true, 200000));
            foreach (int seconds in new[] { 0, 1, 3, 60 })
            {
                var settings = new Settings { FollowCaret = true, FollowSeconds = seconds, ShowFloating = false, ShowTaskbarStatus = false };
                settings.Save(settingsPath);
                Settings restored = Settings.Load(settingsPath);
                check("Follow preferences persist: " + seconds, restored.FollowCaret && restored.FollowSeconds == seconds && !restored.ShowFloating && !restored.ShowTaskbarStatus);
            }
            File.WriteAllText(settingsPath, "FollowCaret=1\nFollowSeconds=-1");
            check("Invalid follow duration defaults to three seconds", Settings.Load(settingsPath).FollowSeconds == 3);
            Size size = new Size(84, 36);
            Rectangle area = new Rectangle(0, 0, 1920, 1080);
            check("Hint appears above right of insertion caret", CaretOverlay.Position(new Rectangle(500, 500, 1, 22), size, area, 6) == new Point(507, 458));
            check("Hint flips left at right edge", CaretOverlay.Position(new Rectangle(1910, 500, 1, 22), size, area, 6) == new Point(1820, 458));
            check("Hint flips below at top edge", CaretOverlay.Position(new Rectangle(500, 0, 1, 22), size, area, 6) == new Point(507, 28));
            check("Follow supports negative monitor coordinates", CaretOverlay.Position(new Rectangle(-1850, 500, 1, 22), size, new Rectangle(-1920, 0, 1920, 1080), 6) == new Point(-1843, 458));
            check("Invalid caret geometry is rejected", !CaretTracker.IsCaretRectangle(Rectangle.Empty) && !CaretTracker.IsCaretRectangle(new Rectangle(10, 10, 1000, 20)));

            using (var dialog = new FollowSettingsForm(true, 3))
            {
                dialog.Show();
                Application.DoEvents();
                bool fits = true;
                foreach (Control control in dialog.Controls) fits &= dialog.ClientRectangle.Contains(control.Bounds);
                check("Follow settings controls fit", fits && dialog.FollowEnabled && dialog.DurationSeconds == 3);
                using (var screenshot = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                    screenshot.Save(Path.Combine(folder, "follow-settings.png"), ImageFormat.Png);
                }
                foreach (Control control in dialog.Controls)
                    if (control is RadioButton && control.Text.StartsWith("一直")) ((RadioButton)control).Checked = true;
                check("Always option disables countdown field", dialog.DurationSeconds == 0);
                foreach (Control control in dialog.Controls)
                    if (control is NumericUpDown) check("Countdown disabled in always mode", !control.Enabled);
                dialog.Close();
            }
            TestNativeCaret(check, folder);
        }

        private static void TestNativeCaret(Action<string, bool> check, string folder)
        {
            using (var editor = new Form { Text = "InputBeacon · 光标跟随预览", ClientSize = new Size(680, 320), StartPosition = FormStartPosition.CenterScreen })
            using (var box = new TextBox { Multiline = true, Location = new Point(26, 80), Size = new Size(624, 208), Font = new Font("Consolas", 15), Text = "ssh user@linux\r\n$ cd /var/log\r\n$ tail -f application.log", BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(247, 248, 250) })
            using (var follower = new CaretOverlay())
            {
                editor.BackColor = box.BackColor;
                editor.Controls.Add(new Label { Text = "提示随输入光标移动 · 不遮挡正在输入的文字", AutoSize = true, Location = new Point(24, 22), Font = new Font("Microsoft YaHei UI", 11) });
                editor.Controls.Add(box);
                editor.Show();
                // The test process is launched with a hidden startup window. Explicitly
                // show only this fixture so Windows creates a visible native caret.
                ShowWindow(editor.Handle, 5);
                editor.Activate();
                box.Focus();
                box.Select(3, 0);
                Application.DoEvents();
                uint process;
                uint thread = Native.GetWindowThreadProcessId(editor.Handle, out process);
                var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf(typeof(Native.GuiThreadInfo)) };
                Rectangle first;
                bool read = Native.GetGUIThreadInfo(thread, ref info);
                check("Read real native TextBox caret", read && CaretTracker.TryNative(info, out first));
                // Assign explicitly because the short-circuit check above does not imply definite assignment.
                CaretTracker.TryNative(info, out first);
                var sample = new CaretSample { Foreground = editor.Handle, Focus = box.Handle, Bounds = first, Valid = true };
                var settings = new Settings { FollowCaret = true, FollowSeconds = 0 };
                var state = new InputState { Mode = InputMode.English };
                IntPtr foreground = Native.GetForegroundWindow();
                follower.UpdateAt(sample, state, settings);
                Application.DoEvents();
                check("Follow window displays without stealing foreground focus", follower.Visible && Native.IsWindowVisible(follower.Handle) && Native.GetForegroundWindow() == foreground);
                long style = Native.GetStyle(follower.Handle);
                check("Follow window is layered and mouse transparent", (style & Native.WS_EX_TRANSPARENT) != 0 && (style & Native.WS_EX_NOACTIVATE) != 0 && (style & Native.WS_EX_LAYERED) != 0);
                Point initial = follower.Location;
                box.Focus();
                box.Select(box.TextLength, 0);
                Application.DoEvents();
                info = new Native.GuiThreadInfo { Size = Marshal.SizeOf(typeof(Native.GuiThreadInfo)) };
                Rectangle last;
                check("Native caret still available after newline movement", Native.GetGUIThreadInfo(thread, ref info) && CaretTracker.TryNative(info, out last));
                CaretTracker.TryNative(info, out last);
                sample.Bounds = last;
                follower.UpdateAt(sample, state, settings);
                check("Always hint follows horizontal and vertical caret movement", last.X != first.X && last.Y > first.Y && follower.Location != initial);
                check("Follow alpha surface reached compositor", follower.SurfaceUpdates >= 2);
                int updates = follower.SurfaceUpdates;
                settings.Opacity = 60;
                follower.UpdateAt(sample, state, settings);
                check("Opacity change updates stationary follow hint", follower.SurfaceUpdates > updates && follower.Opacity == 1);
                using (var screenshot = new Bitmap(editor.Width, editor.Height))
                {
                    editor.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                    // Layered windows are not included by DrawToBitmap. Composite its exact
                    // renderer at the measured native caret position for the documentation preview.
                    using (Graphics graphics = Graphics.FromImage(screenshot))
                    using (Bitmap hint = CardPainter.Render(follower.Size, state))
                        graphics.DrawImageUnscaled(hint, follower.Left - editor.Left, follower.Top - editor.Top);
                    screenshot.Save(Path.Combine(folder, "follow-preview.png"), ImageFormat.Png);
                }
                follower.Hide();
                check("Follow window can hide immediately", !follower.Visible);
                editor.Close();
            }
        }
    }
}
