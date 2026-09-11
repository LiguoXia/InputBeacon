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
            check("Starting timed follow does not invent a state change", !lifetime.ShouldShow(true, 3, true, 100));
            state.Caps = true;
            lifetime.Observe(state, 3, 200);
            check("A real case change starts the first hint", lifetime.ShouldShow(true, 3, true, 200));
            lifetime.Observe(state, 3, 2500);
            check("Moving caret does not extend a timed hint", !lifetime.ShouldShow(true, 3, true, 3200));
            state.Mode = InputMode.Chinese;
            lifetime.Observe(state, 3, 4000);
            check("Single IME sample does not retrigger hint", !lifetime.ShouldShow(true, 3, true, 4000));
            lifetime.Observe(state, 3, 4200);
            check("Confirmed input mode change retriggers hint", lifetime.ShouldShow(true, 3, true, 7199) && !lifetime.ShouldShow(true, 3, true, 7200));
            state.Caps = false;
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
            check("Reapplying follow settings establishes a silent baseline", !lifetime.ShouldShow(true, 3, true, 200000));
            CheckSpuriousTriggers(check);
            foreach (int seconds in new[] { 0, 1, 3, 60 })
            {
                var settings = new Settings { FollowCaret = true, FollowSeconds = seconds, ShowFloating = false, ShowTaskbarStatus = false };
                settings.Save(settingsPath);
                Settings restored = Settings.Load(settingsPath);
                check("Follow preferences persist: " + seconds, restored.FollowCaret && restored.FollowSeconds == seconds && !restored.ShowFloating && !restored.ShowTaskbarStatus);
            }
            File.WriteAllText(settingsPath, "FollowCaret=1\nFollowSeconds=-1");
            check("Invalid follow duration defaults to three seconds", Settings.Load(settingsPath).FollowSeconds == 3);
            Size size = new Size(72, 34);
            Rectangle area = new Rectangle(0, 0, 1920, 1080);
            check("Bubble tail stays close to insertion caret", CaretOverlay.Position(new Rectangle(500, 500, 1, 22), size, area, 1) == new Point(489, 465));
            check("Hint flips left at right edge", CaretOverlay.Position(new Rectangle(1890, 500, 1, 22), size, area, 1) == new Point(1830, 465));
            check("Bubble stays fully inside extreme right edge", CaretOverlay.Position(new Rectangle(1910, 500, 1, 22), size, area, 1).X == 1848);
            check("Hint flips below at top edge", CaretOverlay.Position(new Rectangle(500, 0, 1, 22), size, area, 1) == new Point(489, 23));
            check("Follow supports negative monitor coordinates", CaretOverlay.Position(new Rectangle(-1850, 500, 1, 22), size, new Rectangle(-1920, 0, 1920, 1080), 1) == new Point(-1861, 465));
            check("Invalid caret geometry is rejected", !CaretTracker.IsCaretRectangle(Rectangle.Empty) && !CaretTracker.IsCaretRectangle(new Rectangle(10, 10, 1000, 20)));
            CheckCompatibility(check);
            CheckBubble(check, folder);

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

        private static void CheckSpuriousTriggers(Action<string, bool> check)
        {
            var lifetime = new FollowLifetime();
            var state = new InputState { Mode = InputMode.Chinese, Foreground = new IntPtr(10), Focus = new IntPtr(11) };
            lifetime.Observe(state, 1, 0);
            state.Mode = InputMode.Unknown;
            lifetime.Observe(state, 1, 2000);
            state.Mode = InputMode.Chinese;
            lifetime.Observe(state, 1, 2200);
            lifetime.Observe(state, 1, 2500);
            check("IME failure and recovery never display a bubble", lifetime.TriggerCount == 0 && !lifetime.ShouldShow(true, 1, true, 2500));
            state.FullWidth = true;
            lifetime.Observe(state, 1, 2600);
            state.FullWidth = false;
            lifetime.Observe(state, 1, 2700);
            check("Full-width flag fluctuations do not trigger follow", lifetime.TriggerCount == 0);
            state.Mode = InputMode.English;
            lifetime.Observe(state, 1, 2800);
            state.Mode = InputMode.Chinese;
            lifetime.Observe(state, 1, 2900);
            check("A transient English sample during composition is ignored", lifetime.TriggerCount == 0);
            state.Mode = InputMode.English;
            lifetime.Observe(state, 1, 3000);
            lifetime.Observe(state, 1, 3100);
            check("Mode switch waits for stable evidence", lifetime.TriggerCount == 0);
            lifetime.Observe(state, 1, 3200);
            check("A real stable mode switch displays once", lifetime.TriggerCount == 1 && lifetime.ShouldShow(true, 1, true, 4199));
            state.Focus = new IntPtr(12);
            state.Mode = InputMode.Chinese;
            lifetime.Observe(state, 1, 3300);
            lifetime.Observe(state, 1, 3600);
            check("Focus changes establish a silent baseline and hide old bubble", lifetime.TriggerCount == 1 && !lifetime.ShouldShow(true, 1, true, 3600));
            state.Foreground = new IntPtr(20);
            state.Focus = new IntPtr(21);
            state.Mode = InputMode.Unknown;
            lifetime.Observe(state, 1, 3700);
            state.Mode = InputMode.English;
            lifetime.Observe(state, 1, 3800);
            lifetime.Observe(state, 1, 4100);
            check("Returning from an unknown foreground context is not a switch", lifetime.TriggerCount == 1);
            state.Caps = true;
            lifetime.Observe(state, 1, 4200);
            check("Caps change remains immediate", lifetime.TriggerCount == 2 && lifetime.ShouldShow(true, 1, true, 4200));
            state.Shift = true;
            lifetime.Observe(state, 1, 4300);
            check("Shift case reversal remains immediate", lifetime.TriggerCount == 3);
            state.Caps = false;
            state.Shift = false;
            lifetime.Observe(state, 1, 4400);
            check("Unchanged effective letter case does not extend countdown", lifetime.TriggerCount == 3 && !lifetime.ShouldShow(true, 1, true, 5300));
            check("Always mode is independent of trigger suppression", lifetime.ShouldShow(true, 0, true, 10000));

            state.Mode = InputMode.Chinese;
            lifetime.Observe(state, 1, 11000);
            lifetime.Observe(state, 1, 13000);
            check("A polling pause cannot confirm a pending mode change", lifetime.TriggerCount == 3);
            lifetime.Observe(state, 1, 13200);
            check("Mode can confirm after polling resumes", lifetime.TriggerCount == 4);
            state.Mode = InputMode.Unknown;
            lifetime.Observe(state, 1, 13300);
            state.Mode = InputMode.Chinese;
            lifetime.Observe(state, 1, 13400);
            check("Unknown recovery does not extend an active countdown", lifetime.TriggerCount == 4 && !lifetime.ShouldShow(true, 1, true, 14200));
        }

        private static void CheckCompatibility(Action<string, bool> check)
        {
            Rectangle caret;
            check("UIA accepts collapsed zero-width caret", AutomationCaret.FromBounds(new double[] { 20, 30, 0, 18 }, false, out caret) && caret == new Rectangle(20, 30, 1, 18));
            check("UIA uses character right edge at document end", AutomationCaret.FromBounds(new double[] { -200, 30, 9, 18 }, true, out caret) && caret.X == -191);
            check("UIA rejects empty and multi-line ranges", !AutomationCaret.FromBounds(new double[0], false, out caret) && !AutomationCaret.FromBounds(new double[8], false, out caret));
            check("UIA rejects non-finite and oversized geometry", !AutomationCaret.FromBounds(new double[] { double.NaN, 1, 0, 20 }, false, out caret) && !AutomationCaret.FromBounds(new double[] { 1, 1, 0, 300 }, false, out caret));
            var range = new GeometryRange(3, 3, false);
            check("Empty UIA end range uses preceding character geometry", AutomationCaret.TryRange(range, out caret) && caret == new Rectangle(130, 50, 1, 20));
            check("UIA geometry fallback never moves original caret", range.Start == 3 && range.End == 3);
            range = new GeometryRange(1, 3, false);
            check("Empty UIA middle range expands by endpoint", AutomationCaret.TryRange(range, out caret) && caret.X == 110);
            range = new GeometryRange(0, 3, false);
            check("UIA fallback locates first character", AutomationCaret.TryRange(range, out caret) && caret.X == 100);
            range = new GeometryRange(0, 0, false);
            check("Empty document without geometry does not invent a caret", !AutomationCaret.TryRange(range, out caret));
            range = new GeometryRange(3, 3, true);
            check("Blank final line does not reuse previous line geometry", !AutomationCaret.TryRange(range, out caret));
            var sample = new CaretSample { Foreground = new IntPtr(1), Focus = new IntPtr(2), Timestamp = 1000, Valid = true };
            check("Recent caret sample can be displayed", CaretTracker.Fresh(sample, new IntPtr(1), new IntPtr(2), 1349));
            check("Expired caret disappears", !CaretTracker.Fresh(sample, new IntPtr(1), new IntPtr(2), 1350));
            check("Focus changes never reuse a previous control caret", !CaretTracker.Fresh(sample, new IntPtr(1), new IntPtr(3), 1001) && !CaretTracker.Fresh(sample, new IntPtr(3), new IntPtr(2), 1001));
            check("Java bridge structures match native ABI", Marshal.SizeOf(typeof(JavaCaret.TextInfo)) == 12 && Marshal.SizeOf(typeof(JavaCaret.TextRectangle)) == 16);
            var value = new JavaCaret.TextRectangle { X = -1, Y = -30, Width = 0, Height = 20 };
            check("Java supports caret on negative monitor coordinates", JavaCaret.Valid(value) && JavaCaret.ToCaret(value, false) == new Rectangle(-1, -30, 1, 20));
            value.Height = 0;
            check("Java rejects an unavailable caret", !JavaCaret.Valid(value));
        }

        // Provider fixture that reproduces empty degenerate/expanded ranges.
        // No text or control selection APIs are implemented or called.
        private sealed class GeometryRange : UiaRange
        {
            internal int Start, End;
            private readonly int length;
            private readonly bool finalBlankLine;
            internal GeometryRange(int caret, int length, bool blank) { Start = End = caret; this.length = length; finalBlankLine = blank; }
            public UiaRange Clone() { return (GeometryRange)MemberwiseClone(); }
            public int CompareEndpoints(int endpoint, UiaRange target, int targetEndpoint)
            {
                var other = (GeometryRange)target;
                return (endpoint == 0 ? Start : End).CompareTo(targetEndpoint == 0 ? other.Start : other.End);
            }
            public void ExpandToEnclosingUnit(int unit)
            {
                if (unit == 3) { Start = finalBlankLine && Start == length ? length : 0; End = length; }
            }
            public double[] GetBoundingRectangles()
            {
                return Start == End ? new double[0] : new double[] { 100 + Start * 10, 50, (End - Start) * 10, 20 };
            }
            public int MoveEndpointByUnit(int endpoint, int unit, int count)
            {
                int before = endpoint == 0 ? Start : End;
                int after = Math.Max(0, Math.Min(length, before + count));
                if (endpoint == 0) { Start = after; End = Math.Max(End, Start); }
                else { End = after; Start = Math.Min(Start, End); }
                return after - before;
            }
            public void Compare() { throw new NotSupportedException(); }
            public void FindAttribute() { throw new NotSupportedException(); }
            public void FindText() { throw new NotSupportedException(); }
            public void GetAttributeValue() { throw new NotSupportedException(); }
            public void GetEnclosingElement() { throw new NotSupportedException(); }
            public void GetText() { throw new NotSupportedException(); }
            public void Move() { throw new NotSupportedException(); }
        }

        private static void CheckBubble(Action<string, bool> check, string folder)
        {
            using (var preview = new Bitmap(480, 220))
            using (Graphics g = Graphics.FromImage(preview))
            {
                g.Clear(Color.FromArgb(244, 245, 248));
                using (var dark = new SolidBrush(Color.FromArgb(40, 43, 50))) g.FillRectangle(dark, 240, 0, 240, 220);
                for (int i = 0; i < 4; i++)
                    using (Bitmap bubble = BubblePainter.Render(new Size(108, 51), new InputState { Mode = i % 2 == 0 ? InputMode.Chinese : InputMode.English, Caps = i > 1 }, null, i % 2 != 0, i > 1))
                    {
                        check("Bubble transparent corners " + i, bubble.GetPixel(0, 0).A < 20 && bubble.GetPixel(bubble.Width - 1, bubble.Height - 1).A < 20);
                        check("Bubble panel remains translucent " + i, bubble.GetPixel(54, 20).A > 200 && bubble.GetPixel(54, 20).A < 255);
                        g.DrawImageUnscaled(bubble, i % 2 * 240 + 66, i / 2 * 100 + 28);
                    }
                preview.Save(Path.Combine(folder, "bubble-preview.png"), ImageFormat.Png);
            }
            using (var dialog = new JavaSupportForm())
            {
                dialog.Show();
                Application.DoEvents();
                bool fits = true;
                foreach (Control control in dialog.Controls) fits &= dialog.ClientRectangle.Contains(control.Bounds);
                check("Java support dialog controls fit", fits);
                dialog.Close();
            }
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
                // Exercise the actual native COM interface and its method order on
                // our own control. Keep pumping the UI while its MTA client runs.
                string automationStatus = null;
                Rectangle automationBounds = Rectangle.Empty;
                IntPtr editorHandle = editor.Handle, boxHandle = box.Handle;
                var automationThread = new System.Threading.Thread(delegate()
                {
                    using (var reader = new AutomationCaret())
                    {
                        reader.TryReadControl(editorHandle, boxHandle, out automationBounds);
                        automationStatus = reader.Status;
                    }
                });
                automationThread.SetApartmentState(System.Threading.ApartmentState.MTA);
                automationThread.IsBackground = true;
                automationThread.Start();
                long deadline = CaretTracker.Now + 5000;
                while (automationThread.IsAlive && CaretTracker.Now < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                File.WriteAllText(Path.Combine(folder, "uia-fixture.txt"), automationStatus + " " + automationBounds);
                check("Native UIA COM client reaches our real TextBox provider", !automationThread.IsAlive && (automationStatus == "UIA TextPattern2" || automationStatus == "UIA: TextPattern2 unavailable" || automationStatus == "UIA: caret inactive"));
                if (automationStatus == "UIA TextPattern2") check("TextPattern2 agrees with native insertion caret", Math.Abs(automationBounds.X - first.X) <= 2 && Math.Abs(automationBounds.Y - first.Y) <= 2);
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
                    using (Bitmap hint = BubblePainter.Render(follower.Size, state, null, false, false))
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
