using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace InputBeacon
{
    internal static class SelfTests
    {
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
        private static readonly List<string> Results = new List<string>();
        private static void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException(name);
            Results.Add("PASS " + name);
        }

        internal static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            try
            {
                Check("English keyboard layout", InputState.Resolve(0x0409, false, false, 0, false, 0).Mode == InputMode.English);
                Check("Chinese native bit with extra flags", InputState.Resolve(0x0804, true, true, 1, true, 0x401).Mode == InputMode.Chinese);
                Check("Chinese IME in English mode", InputState.Resolve(0x0804, true, true, 1, true, 0x400).Mode == InputMode.English);
                Check("Closed IME despite stale native bit", InputState.Resolve(0x0804, true, true, 0, true, 1).Mode == InputMode.English);
                Check("Unavailable Chinese IME is UNKNOWN", InputState.Resolve(0x0804, true, false, 0, false, 0).Mode == InputMode.Unknown);
                Check("Missing conversion is not assumed Chinese", InputState.Resolve(0x0804, true, true, 1, false, 0).Mode == InputMode.Unknown);
                Check("Conversion fallback without open flag", InputState.Resolve(0x0804, true, false, 0, true, 1).Mode == InputMode.Chinese);
                Check("Traditional Chinese", InputState.Resolve(0x0404, true, true, 1, true, 1).Mode == InputMode.Chinese);
                Check("Full width English", InputState.Resolve(0x0804, true, true, 1, true, 8).FullWidth);
                Check("German is not mislabeled English", InputState.Resolve(0x0407, false, false, 0, false, 0).Mode == InputMode.Other);
                Check("Japanese is not mislabeled Chinese", InputState.Resolve(0x0411, true, true, 1, true, 1).OtherLabel == "日文");
                Check("No layout is UNKNOWN", InputState.Resolve(0, false, false, 0, false, 0).Mode == InputMode.Unknown);
                foreach (bool caps in new[] { false, true })
                    foreach (bool shift in new[] { false, true })
                    {
                        var state = new InputState { Caps = caps, Shift = shift };
                        Check("Caps=" + caps + " Shift=" + shift + " letter case", state.Uppercase == (caps != shift));
                    }

                string settingsPath = Path.Combine(folder, "test-settings.ini");
                File.WriteAllText(settingsPath, "Scale=-40\nOpacity=999\nX=bad\nClickThrough=hello\nUnrecognized=12", Encoding.UTF8);
                Settings settings = Settings.Load(settingsPath);
                Check("Corrupt settings use safe defaults", settings.Scale == 100 && settings.Opacity == 94 && settings.X == int.MinValue && !settings.ClickThrough);
                settings.X = -850; settings.Y = 150; settings.Scale = 125; settings.ClickThrough = true;
                Check("Settings atomic save", settings.Save(settingsPath));
                Settings loaded = Settings.Load(settingsPath);
                Check("Settings round trip", loaded.X == -850 && loaded.Y == 150 && loaded.Scale == 125 && loaded.ClickThrough);
                CheckColors(folder, settingsPath);
                CheckTaskbar(folder, settingsPath);
                FollowTests.Run(Check, folder, settingsPath);
                Check("Disconnected screen position recovery", Settings.Clamp(new Point(9000, -9000), new Size(296, 84), new Rectangle(0, 0, 1920, 1080)) == new Point(1624, 0));
                Check("Negative-coordinate monitor", Settings.Clamp(new Point(-1900, 50), new Size(296, 84), new Rectangle(-1920, 0, 1920, 1080)) == new Point(-1900, 50));
                uint answer;
                Check("Missing IME handle returns failure", !Native.QueryIme(IntPtr.Zero, Native.IMC_GETOPENSTATUS, out answer));
                Check("Destroyed or invalid IME handle returns failure", !Native.QueryIme(new IntPtr(0x123456), Native.IMC_GETOPENSTATUS, out answer));
                TestNativeMessages();

                IntPtr foreground = Native.GetForegroundWindow();
                using (var form = new Overlay(new Settings(), null, true))
                {
                    form.Show();
                    Application.DoEvents();
                    long style = Native.GetStyle(form.Handle);
                    Check("Overlay window is actually visible", form.Visible && Native.IsWindowVisible(form.Handle));
                    Check("Overlay does not take foreground focus", Native.GetForegroundWindow() == foreground);
                    Check("Overlay NOACTIVATE + TOOLWINDOW styles", (style & Native.WS_EX_NOACTIVATE) != 0 && (style & Native.WS_EX_TOOLWINDOW) != 0);
                    Check("Overlay stays topmost", (style & 8) != 0);
                    Check("Per-pixel alpha reached the Windows compositor", form.SurfaceUpdates > 0);
                    Check("No hard window region clips antialiasing", form.Region == null);
                    form.SetPreviewState(new InputState { Mode = InputMode.English });
                    using (var actual = form.CopySurface())
                    {
                        actual.Save(Path.Combine(folder, "window.png"), ImageFormat.Png);
                        CheckTransparency(actual);
                    }
                    foreach (ToolStripItem item in form.ContextMenuStrip.Items)
                        if (item.Text == "文字不透明度")
                            foreach (ToolStripMenuItem choice in ((ToolStripMenuItem)item).DropDownItems) choice.PerformClick();
                    Check("Changing opacity preserves per-pixel rendering", form.Opacity == 1 && form.SurfaceUpdates >= 4);
                    uint before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                    for (int i = 0; i < 80; i++)
                        form.SetPreviewState(new InputState { Mode = i % 2 == 0 ? InputMode.English : InputMode.Chinese, Caps = i % 3 == 0 });
                    uint after = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                    Check("Repeated alpha updates release GDI resources", after <= before + 4);
                    form.Hide();
                    int updates = form.SurfaceUpdates;
                    form.Show();
                    Check("Hidden overlay restores its alpha surface", form.SurfaceUpdates > updates);
                    form.Hide();
                }
                var transparentSettings = new Settings { ClickThrough = true };
                using (var form = new Overlay(transparentSettings, null, true))
                {
                    Check("Mouse passthrough enabled", (Native.GetStyle(form.Handle) & Native.WS_EX_TRANSPARENT) != 0);
                    transparentSettings.ClickThrough = false;
                    form.ApplyClickThrough();
                    Check("Mouse passthrough can be recovered", (Native.GetStyle(form.Handle) & Native.WS_EX_TRANSPARENT) == 0);
                }
                RenderPreview(folder);
                string diagnostic;
                InputState live = new InputProbe().Read(out diagnostic);
                File.WriteAllText(Path.Combine(folder, "native-probe.txt"), diagnostic + "\r\nCaps=" + live.Caps + "\r\nShift=" + live.Shift, Encoding.UTF8);
                Check("Live Win32 probe completed", true);
                Results.Add("Test scope: synthetic IME message endpoint and local WinForms overlay. Browser / SSH end-to-end input switching is a separate manual check.");
                File.WriteAllLines(Path.Combine(folder, "results.txt"), Results, Encoding.UTF8);
                return 0;
            }
            catch (Exception error)
            {
                Results.Add("FAIL " + error.ToString());
                File.WriteAllLines(Path.Combine(folder, "results.txt"), Results, Encoding.UTF8);
                return 1;
            }
        }

        private static void CheckTransparency(Bitmap bitmap)
        {
            int clear = 0;
            var levels = new HashSet<byte>();
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte alpha = bitmap.GetPixel(x, y).A;
                    if (alpha == 0) clear++;
                    if (alpha > 0 && alpha < 255) levels.Add(alpha);
                }
            Check("Background is fully transparent with no filled panel", clear > bitmap.Width * bitmap.Height * 0.70);
            Check("Corners have no opaque or tinted background", bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A == 0);
            Check("Glyph edges retain smooth fractional alpha", levels.Count >= 32);
            Check("Surface uses premultiplied 32-bit alpha", bitmap.PixelFormat == PixelFormat.Format32bppPArgb);
        }

        private static void CheckColors(string folder, string path)
        {
            Color parsed;
            Check("HEX RRGGBB", ColorValue.TryParse("#12C34E", out parsed) && parsed.ToArgb() == Color.FromArgb(0x12, 0xc3, 0x4e).ToArgb());
            Check("HEX shorthand and trim", ColorValue.TryParse("  #aB3  ", out parsed) && ColorValue.Hex(parsed) == "#AABB33");
            Check("HEX without hash", ColorValue.TryParse("ffffff", out parsed) && parsed.ToArgb() == Color.White.ToArgb());
            foreach (string invalid in new[] { "", "#", "#GGG", "#12345", "#12345678", "0x00FF00", "#12 345", "red" })
                Check("Reject invalid HEX '" + invalid + "'", !ColorValue.TryParse(invalid, out parsed));
            var settings = new Settings { UseCustomTextColor = true, TextColor = Color.FromArgb(0x12, 0xc3, 0x4e) };
            settings.Save(path);
            Settings restored = Settings.Load(path);
            Check("Custom color persists across restart", restored.UseCustomTextColor && restored.TextColor.ToArgb() == settings.TextColor.ToArgb());
            using (Bitmap color = CardPainter.Render(new Size(168, 72), new InputState { Mode = InputMode.Chinese, Caps = true }, restored.TextColor))
            {
                int left = 0, right = 0;
                for (int y = 0; y < color.Height; y++)
                    for (int x = 0; x < color.Width; x++)
                    {
                        Color sample = color.GetPixel(x, y);
                        if (sample.A > 245 && Math.Abs(sample.R - 0x12) < 6 && Math.Abs(sample.G - 0xc3) < 6 && Math.Abs(sample.B - 0x4e) < 6)
                        { if (x < color.Width / 2) left++; else right++; }
                    }
                Check("Chosen HEX colors both language and case glyphs", left > 10 && right > 10);
                color.Save(Path.Combine(folder, "custom-color.png"), ImageFormat.Png);
            }
            using (var dialog = new ColorSettingsForm(true, restored.TextColor))
            {
                dialog.Show();
                Application.DoEvents();
                using (var image = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(folder, "color-settings.png"), ImageFormat.Png);
                }
                TextBox hex = null;
                Button apply = null;
                foreach (Control control in dialog.Controls)
                {
                    if (control is TextBox) hex = (TextBox)control;
                    if (control is Button && control.Text == "应用") apply = (Button)control;
                }
                hex.Text = "#INVALID";
                Check("Invalid HEX disables Apply", !apply.Enabled);
                hex.Text = "#ABC";
                Check("Valid HEX re-enables Apply and updates selection", apply.Enabled && ColorValue.Hex(dialog.SelectedColor) == "#AABBCC");
                Check("Editing the picker does not mutate saved settings before Apply", restored.TextColor.ToArgb() == settings.TextColor.ToArgb());
                bool controlsFit = true;
                foreach (Control control in dialog.Controls) controlsFit &= dialog.ClientRectangle.Contains(control.Bounds);
                Check("Color picker controls fit without clipping", controlsFit);
                dialog.Close();
            }
        }

        private static void CheckTaskbar(string folder, string path)
        {
            using (var menu = new ContextMenuStrip())
            using (var tray = new StatusTray(menu, delegate { }))
            {
                foreach (bool floating in new[] { false, true })
                    foreach (bool taskbar in new[] { false, true })
                    {
                        var settings = new Settings { ShowFloating = floating, ShowTaskbarStatus = taskbar };
                        settings.Save(path);
                        Settings restored = Settings.Load(path);
                        Check("Display preferences persist: floating=" + floating + " taskbar=" + taskbar,
                            restored.ShowFloating == floating && restored.ShowTaskbarStatus == taskbar);
                        tray.Update(new InputState { Mode = InputMode.Chinese, Caps = true }, restored);
                        using (var form = new Overlay(restored, null, true))
                        {
                            form.Show();
                            Application.DoEvents();
                            Check("Independent display switches: floating=" + floating + " taskbar=" + taskbar,
                                form.Visible == floating && tray.CaseIconVisible == taskbar && tray.StatusVisible == taskbar);
                            Check("Settings entry remains recoverable", tray.ControlIconVisible);
                            if (!floating)
                            {
                                foreach (ToolStripItem item in form.ContextMenuStrip.Items)
                                    if (item.Text == "显示悬浮窗") ((ToolStripMenuItem)item).PerformClick();
                                Check("Tray-only startup can restore floating window", form.Visible && restored.ShowFloating);
                            }
                            form.Hide();
                        }
                    }
                var colors = new Settings { ShowTaskbarStatus = true, UseCustomTextColor = true, TextColor = Color.FromArgb(18, 195, 78) };
                tray.Update(new InputState { Mode = InputMode.English }, colors);
                uint resources = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                for (int i = 0; i < 60; i++)
                    tray.Update(new InputState { Mode = i % 2 == 0 ? InputMode.Chinese : InputMode.English, Caps = i % 3 == 0 }, colors);
                Check("Taskbar status updates release old icon handles", GetGuiResources(Process.GetCurrentProcess().Handle, 1) <= resources + 4);
                tray.RestoreAfterExplorerRestart();
                Check("Taskbar status recreates after Explorer notification", tray.ControlIconVisible && tray.CaseIconVisible);
            }
            using (var image = new Bitmap(384, 132))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.FromArgb(245, 246, 248));
                using (var background = new SolidBrush(Color.FromArgb(30, 32, 38))) graphics.FillRectangle(background, 192, 0, 192, 132);
                for (int i = 0; i < 4; i++)
                {
                    var state = new InputState { Mode = i % 2 == 0 ? InputMode.Chinese : InputMode.English, Caps = i % 2 == 0 };
                    using (Icon icon = StatusTray.CreateIcon(state, i >= 2, null))
                    {
                        Check("Native taskbar icon is valid 32px with a handle", icon.Width == 32 && icon.Height == 32 && icon.Handle != IntPtr.Zero);
                        graphics.DrawIconUnstretched(icon, new Rectangle(32 + i % 2 * 80, 24 + i / 2 * 56, 32, 32));
                        graphics.DrawIconUnstretched(icon, new Rectangle(224 + i % 2 * 80, 24 + i / 2 * 56, 32, 32));
                    }
                }
                image.Save(Path.Combine(folder, "taskbar-icons.png"), ImageFormat.Png);
            }
        }

        private sealed class ImeTestWindow : NativeWindow
        {
            internal volatile int Reply = 1;
            internal volatile int Delay;
            internal ImeTestWindow() { CreateHandle(new CreateParams { Caption = "InputBeacon test endpoint", Parent = new IntPtr(-3) }); }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == Native.WM_IME_CONTROL)
                {
                    int delay = Delay;
                    if (delay > 0) Thread.Sleep(delay);
                    message.Result = new IntPtr(Reply);
                    return;
                }
                if (message.Msg == 0x0010) { DestroyHandle(); Application.ExitThread(); return; }
                base.WndProc(ref message);
            }
        }

        private static void TestNativeMessages()
        {
            ImeTestWindow window = null;
            IntPtr handle = IntPtr.Zero;
            using (var ready = new ManualResetEvent(false))
            {
                var thread = new Thread(delegate()
                {
                    window = new ImeTestWindow();
                    handle = window.Handle;
                    ready.Set();
                    Application.Run();
                }) { IsBackground = true };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                if (!ready.WaitOne(5000)) throw new TimeoutException("Test endpoint startup");
                try
                {
                    uint answer;
                    Check("Cross-thread IME response", Native.QueryIme(handle, Native.IMC_GETCONVERSIONMODE, out answer) && answer == 1);
                    window.Reply = 0;
                    Check("Zero is a valid English response, not timeout", Native.QueryIme(handle, Native.IMC_GETCONVERSIONMODE, out answer) && answer == 0);
                    window.Reply = -1;
                    Check("IME error response rejected", !Native.QueryIme(handle, Native.IMC_GETCONVERSIONMODE, out answer));
                    window.Delay = 250;
                    var watch = Stopwatch.StartNew();
                    bool result = Native.QueryIme(handle, Native.IMC_GETCONVERSIONMODE, out answer);
                    watch.Stop();
                    Check("Hung target has bounded timeout", !result && watch.ElapsedMilliseconds < 200);
                }
                finally
                {
                    Native.PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    if (!thread.Join(3000)) throw new TimeoutException("Test endpoint cleanup");
                }
            }
        }

        private static void RenderPreview(string folder)
        {
            var examples = new[]
            {
                new InputState { Mode = InputMode.English },
                new InputState { Mode = InputMode.Chinese },
                new InputState { Mode = InputMode.English, Caps = true },
                new InputState { Mode = InputMode.English, Shift = true },
                new InputState { Mode = InputMode.English, Caps = true, Shift = true },
                new InputState { Mode = InputMode.Unknown },
                new InputState { Mode = InputMode.English, FullWidth = true },
                new InputState { Mode = InputMode.Chinese, Caps = true }
            };
            using (var sheet = new Bitmap(720, 416))
            using (Graphics graphics = Graphics.FromImage(sheet))
            using (var font = new Font("Microsoft YaHei UI", 14, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.FromArgb(246, 247, 249));
                graphics.DrawString("InputBeacon 1.4.1  ·  透明极简版", font, Brushes.DimGray, 30, 22);
                graphics.DrawString("以下仅为浅色 / 深色背景对比，程序本身没有底板", font, Brushes.Gray, 30, 52);
                using (var dark = new SolidBrush(Color.FromArgb(25, 28, 34))) graphics.FillRectangle(dark, 360, 92, 360, 324);
                for (int i = 0; i < examples.Length; i++)
                {
                    using (var card = CardPainter.Render(new Size(CardPainter.Width, CardPainter.Height), examples[i]))
                    {
                        int column = i % 2, row = i / 2;
                        graphics.DrawImageUnscaled(card, 50 + column * 145, 124 + row * 70);
                        graphics.DrawImageUnscaled(card, 410 + column * 145, 124 + row * 70);
                    }
                }
                sheet.Save(Path.Combine(folder, "preview.png"), ImageFormat.Png);
            }
            foreach (int scale in new[] { 80, 100, 125, 150, 200 })
            {
                using (var card = CardPainter.Render(new Size(CardPainter.Width * scale / 100, CardPainter.Height * scale / 100), examples[4]))
                {
                    card.Save(Path.Combine(folder, "scale-" + scale + ".png"), ImageFormat.Png);
                }
            }
            Check("Rendered eight states and five display scales", true);
        }
    }
}
