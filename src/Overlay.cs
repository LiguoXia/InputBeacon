using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InputBeacon
{
    internal sealed class Overlay : Form
    {
        private readonly Settings settings;
        private readonly InputProbe probe = new InputProbe();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly EventWaitHandle showRequest;
        private readonly bool testMode;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolTip tooltip = new ToolTip();
        private StatusTray tray;
        private InputState state = new InputState();
        private ToolStripMenuItem visibleItem, clickThroughItem, startupItem, taskbarItem, followItem;
        private readonly FollowLifetime followLifetime = new FollowLifetime();
        private CaretTracker caretTracker;
        private CaretOverlay caretOverlay;
        private Point dragStart, windowStart;
        private bool dragging;
        private bool cleanedUp;
        private bool surfaceReady;
        private bool presenting;
        private bool settingsDialogOpen;
        private Bitmap surface;
        internal int SurfaceUpdates { get; private set; }
        private float dpi = 96;
        private int ticks;
        private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

        internal Overlay(Settings settings, EventWaitHandle showRequest, bool testMode)
        {
            this.settings = settings;
            this.showRequest = showRequest;
            this.testMode = testMode;
            Text = "键盘状态 · InputBeacon";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            // Form.Opacity invokes SetLayeredWindowAttributes, which is incompatible
            // with per-pixel UpdateLayeredWindow. Alpha is applied by LayeredSurface.
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            using (Graphics graphics = CreateGraphics()) dpi = graphics.DpiX;
            ApplySize();
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = settings.X == int.MinValue || settings.Y == int.MinValue ?
                new Point(area.Right - Width - 24, area.Top + 70) :
                Settings.Clamp(new Point(settings.X, settings.Y), Size, Screen.FromPoint(new Point(settings.X, settings.Y)).WorkingArea);
            surfaceReady = true;
            UpdateTooltip();
            BuildMenu();
            ContextMenuStrip = menu;
            if (!testMode)
            {
                tray = new StatusTray(menu, ToggleVisible);
                tray.Update(state, settings);
                SystemEvents.DisplaySettingsChanged += DisplayChanged;
                SystemEvents.SessionSwitch += SessionChanged;
                timer.Interval = 100;
                timer.Tick += Tick;
                timer.Start();
                RefreshState();
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value && (settings == null || settings.ShowFloating));
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED;
                if (settings != null && settings.ClickThrough) value.ExStyle |= Native.WS_EX_TRANSPARENT;
                return value;
            }
        }

        internal void SetPreviewState(InputState value) { state = value; RebuildSurface(); }

        internal Bitmap CopySurface()
        {
            if (surface == null) RebuildSurface();
            return surface.Clone(new Rectangle(Point.Empty, surface.Size), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }

        private void RebuildSurface()
        {
            if (!surfaceReady || cleanedUp || ClientSize.Width < 1 || ClientSize.Height < 1) return;
            Bitmap next = CardPainter.Render(ClientSize, state, settings.UseCustomTextColor ? (Color?)settings.TextColor : null);
            if (surface != null) surface.Dispose();
            surface = next;
            PresentSurface();
        }

        private void PresentSurface()
        {
            if (presenting || !surfaceReady || cleanedUp || !IsHandleCreated || surface == null) return;
            presenting = true;
            try
            {
                LayeredSurface.Present(Handle, Location, surface, settings.Opacity);
                SurfaceUpdates++;
            }
            finally { presenting = false; }
        }

        private void UpdateTooltip()
        {
            tooltip.SetToolTip(this, state.ModeTitle + " · " + state.CaseTitle + "\n" + state.CaseDetail +
                (state.FullWidth ? "\n全角输入" : "") + "\n拖动文字调整位置 · 右键设置");
        }

        private void BuildMenu()
        {
            menu.Font = new Font("Microsoft YaHei UI", 9);
            menu.Items.Add(new ToolStripMenuItem("键盘状态  InputBeacon") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            visibleItem = new ToolStripMenuItem("显示悬浮窗", null, delegate { ToggleVisible(); });
            menu.Items.Add(visibleItem);
            taskbarItem = new ToolStripMenuItem("任务栏显示状态（中/英 · A/a）", null, delegate
            {
                settings.ShowTaskbarStatus = !settings.ShowTaskbarStatus;
                if (tray != null) tray.Update(state, settings);
                SaveSettings();
            });
            menu.Items.Add(taskbarItem);
            followItem = new ToolStripMenuItem("光标跟随显示", null, delegate
            {
                settings.FollowCaret = !settings.FollowCaret;
                followLifetime.Reset();
                if (!settings.FollowCaret) StopFollowing();
                SaveSettings();
            });
            menu.Items.Add(followItem);
            menu.Items.Add("跟随显示设置…", null, delegate { ShowFollowSettings(); });
            menu.Items.Add("IDEA / Java 光标支持…", null, delegate
            {
                settingsDialogOpen = true;
                if (caretOverlay != null) caretOverlay.Hide();
                try { using (var dialog = new JavaSupportForm()) dialog.ShowDialog(this); }
                finally { settingsDialogOpen = false; followLifetime.Reset(); }
            });
            clickThroughItem = new ToolStripMenuItem("鼠标穿透（从托盘取消）", null, delegate
            {
                settings.ClickThrough = !settings.ClickThrough;
                ApplyClickThrough();
                SaveSettings();
            });
            menu.Items.Add(clickThroughItem);
            var sizeMenu = new ToolStripMenuItem("显示大小");
            foreach (int value in new[] { 80, 100, 125, 150 })
            {
                int selected = value;
                var item = new ToolStripMenuItem(value + "%", null, delegate
                {
                    settings.Scale = selected;
                    ApplySize();
                    ClampToScreen();
                    SaveSettings();
                }) { Tag = value };
                sizeMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(sizeMenu);
            var opacityMenu = new ToolStripMenuItem("文字不透明度");
            foreach (int value in new[] { 60, 80, 94, 100 })
            {
                int selected = value;
                opacityMenu.DropDownItems.Add(new ToolStripMenuItem(value + "%", null, delegate
                {
                    settings.Opacity = selected;
                    PresentSurface();
                    SaveSettings();
                }) { Tag = value });
            }
            menu.Items.Add(opacityMenu);
            menu.Items.Add("文字颜色…", null, delegate { ShowColorSettings(); });
            menu.Items.Add("重置位置", null, delegate { ResetPosition(); });
            menu.Items.Add(new ToolStripSeparator());
            startupItem = new ToolStripMenuItem("开机启动", null, delegate
            {
                try { Settings.SetStartsWithWindows(!Settings.StartsWithWindows); }
                catch (Exception error)
                {
                    MessageBox.Show("无法修改开机启动设置：\n" + error.Message, "键盘状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            });
            menu.Items.Add(startupItem);
            menu.Items.Add("使用说明", null, delegate
            {
                MessageBox.Show("键盘状态  v1.4.4\n\n中 / 英：当前窗口的中文 / 英文输入模式。\nA / a：英文字母大写 / 小写。\n字母下的小圆点：Caps Lock 已开启。\n小箭头 ↑：按住 Shift，大小写临时反转。\n中 / 英下的小圆点：全角输入。\n?：暂未读到输入法状态。\n\n右键 → 文字颜色：预设色、调色盘或 HEX 色值。\n右键 → 任务栏显示状态：显示中/英和 A/a 两个图标。\n右键 → 跟随显示设置：开启光标右上方提示。\n支持切换后 1～60 秒消失，或一直跟随光标。\n右键 → IDEA / Java 光标支持：启用后重新打开 IDEA。\n三种显示方式独立开关，退出后自动记住。\n图标在任务栏右侧通知区域，可能收入“^”菜单。\n可以把图标从“^”拖出来常显。\n\n悬浮窗背景完全透明；拖动文字改变位置。\n双击任一托盘图标可隐藏 / 显示悬浮窗。\n再次运行 exe 可找回窗口。\n\n无需安装、无需联网。不会保存输入内容。", "键盘状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            menu.Items.Add("复制状态诊断", null, delegate
            {
                try { Clipboard.SetText("InputBeacon 1.4.4\r\n" + probe.GetDiagnostic() + "\r\n" + MemoryDiagnostics.Read() + "\r\nCaps=" + state.Caps + "\r\nShift=" + state.Shift + "\r\nFollow=" + settings.FollowCaret + "\r\nFollowSeconds=" + settings.FollowSeconds + "\r\nFollowTriggers=" + followLifetime.TriggerCount + "\r\nLastFollowTrigger=" + followLifetime.LastTrigger + "\r\n" + (caretTracker == null ? "Caret tracker idle" : caretTracker.Status)); }
                catch (System.Runtime.InteropServices.ExternalException) { }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            menu.Opening += delegate
            {
                visibleItem.Checked = Visible;
                taskbarItem.Checked = settings.ShowTaskbarStatus;
                followItem.Checked = settings.FollowCaret;
                if (caretOverlay != null) caretOverlay.Hide();
                clickThroughItem.Checked = settings.ClickThrough;
                startupItem.Checked = Settings.StartsWithWindows;
                foreach (ToolStripMenuItem item in sizeMenu.DropDownItems) item.Checked = (int)item.Tag == settings.Scale;
                foreach (ToolStripMenuItem item in opacityMenu.DropDownItems) item.Checked = (int)item.Tag == settings.Opacity;
            };
        }

        private void Tick(object sender, EventArgs e)
        {
            if (showRequest != null && showRequest.WaitOne(0))
            {
                settings.ClickThrough = false;
                settings.ShowFloating = true;
                ApplyClickThrough();
                Show();
                ClampToScreen();
                SaveSettings();
            }
            if (!menu.Visible && !settingsDialogOpen) RefreshState();
            UpdateFollow();
            if (++ticks % 20 == 0 && Visible) KeepOnTop();
        }

        private void UpdateFollow()
        {
            if (!settings.FollowCaret || menu.Visible || settingsDialogOpen)
            {
                if (caretOverlay != null) caretOverlay.Hide();
                return;
            }
            long now = CaretTracker.Now;
            followLifetime.Observe(state, settings.FollowSeconds, now);
            // Keep the single background provider connected even between hints.
            // Otherwise a cold UIA connection can consume a one-second countdown.
            if (caretTracker == null) caretTracker = new CaretTracker();
            CaretSample caret = caretTracker.Read();
            followLifetime.ObserveCaret(state, caret, settings.FollowSeconds, now);
            if (!followLifetime.ShouldShow(true, settings.FollowSeconds, caret != null, now) ||
                (state.Foreground != IntPtr.Zero && (caret.Foreground != state.Foreground || caret.Focus != state.Focus)))
            {
                if (caretOverlay != null) caretOverlay.Hide();
                return;
            }
            if (caretOverlay == null) caretOverlay = new CaretOverlay();
            caretOverlay.UpdateAt(caret, state, settings);
        }

        private void StopFollowing()
        {
            if (caretOverlay != null) { caretOverlay.Dispose(); caretOverlay = null; }
            // Keep the single idle worker until exit. Repeatedly toggling the feature
            // must not create more workers if an external provider has stopped responding.
        }

        private void ShowFollowSettings()
        {
            settingsDialogOpen = true;
            if (caretOverlay != null) caretOverlay.Hide();
            try
            {
                using (var dialog = new FollowSettingsForm(settings.FollowCaret, settings.FollowSeconds))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        settings.FollowCaret = dialog.FollowEnabled;
                        settings.FollowSeconds = dialog.DurationSeconds;
                        followLifetime.Reset();
                        if (!settings.FollowCaret) StopFollowing();
                        SaveSettings();
                    }
            }
            finally { settingsDialogOpen = false; }
        }

        private void ShowColorSettings()
        {
            settingsDialogOpen = true;
            if (caretOverlay != null) caretOverlay.Hide();
            try
            {
                using (var dialog = new ColorSettingsForm(settings.UseCustomTextColor, settings.TextColor))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        settings.UseCustomTextColor = dialog.UseCustomColor;
                        settings.TextColor = dialog.SelectedColor;
                        RebuildSurface();
                        if (tray != null) tray.Update(state, settings);
                        SaveSettings();
                    }
            }
            finally { settingsDialogOpen = false; }
        }

        private void RefreshState()
        {
            InputState next = probe.Read();
            bool changed = !next.SameDisplay(state);
            state = next;
            if (!changed) return;
            RebuildSurface();
            UpdateTooltip();
            if (tray != null) tray.Update(state, settings);
        }

        internal void ApplyClickThrough()
        {
            if (!IsHandleCreated) return;
            long style = Native.GetStyle(Handle);
            style = settings.ClickThrough ? style | Native.WS_EX_TRANSPARENT : style & ~Native.WS_EX_TRANSPARENT;
            Native.SetStyle(Handle, style);
            KeepOnTop();
        }

        private void ApplySize()
        {
            float scale = dpi / 96f * settings.Scale / 100f;
            ClientSize = new Size((int)Math.Round(CardPainter.Width * scale), (int)Math.Round(CardPainter.Height * scale));
            RebuildSurface();
        }

        private void ClampToScreen() { Location = Settings.Clamp(Location, Size, Screen.FromRectangle(Bounds).WorkingArea); }
        private void KeepOnTop() { Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
        private void ToggleVisible()
        {
            settings.ShowFloating = !Visible;
            if (Visible) Hide(); else { Show(); ClampToScreen(); KeepOnTop(); }
            SaveSettings();
        }
        private void ResetPosition()
        {
            settings.ShowFloating = true;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = Settings.Clamp(new Point(area.Right - Width - 24, area.Top + 70), Size, area);
            Show();
            SaveSettings();
        }
        private void SaveSettings()
        {
            if (testMode) return;
            settings.X = Left;
            settings.Y = Top;
            if (!settings.Save(Settings.FilePath) && tray != null)
                tray.SaveWarning();
        }

        private void DisplayChanged(object sender, EventArgs e)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { ClampToScreen(); });
        }
        private void SessionChanged(object sender, SessionSwitchEventArgs e)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { RefreshState(); });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { dpi = Native.GetDpiForWindow(Handle); } catch (EntryPointNotFoundException) { }
            if (dpi <= 0) dpi = 96;
            ApplySize();
            ClampToScreen();
            ApplyClickThrough();
            PresentSurface();
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { if (surface == null) RebuildSurface(); else PresentSurface(); }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragStart = Cursor.Position;
            windowStart = Location;
            Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Point cursor = Cursor.Position;
            Location = new Point(windowStart.X + cursor.X - dragStart.X, windowStart.Y + cursor.Y - dragStart.Y);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            Capture = false;
            ClampToScreen();
            SaveSettings();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture || !dragging) return;
            dragging = false;
            ClampToScreen();
            SaveSettings();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (message.Msg == 0x02e0) // WM_DPICHANGED
            {
                dpi = message.WParam.ToInt64() & 0xffff;
                Native.Rect suggested = (Native.Rect)System.Runtime.InteropServices.Marshal.PtrToStructure(message.LParam, typeof(Native.Rect));
                Location = new Point(suggested.Left, suggested.Top);
                ApplySize();
                return;
            }
            base.WndProc(ref message);
            if ((uint)message.Msg == taskbarCreated && tray != null) tray.RestoreAfterExplorerRestart();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !cleanedUp)
            {
                cleanedUp = true;
                timer.Stop();
                timer.Dispose();
                StopFollowing();
                if (caretTracker != null) caretTracker.Dispose();
                if (!testMode)
                {
                    SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                    SystemEvents.SessionSwitch -= SessionChanged;
                    SaveSettings();
                }
                if (tray != null) tray.Dispose();
                if (surface != null) surface.Dispose();
                tooltip.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
