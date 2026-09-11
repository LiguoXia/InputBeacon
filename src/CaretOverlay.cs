using System;
using System.Drawing;
using System.Windows.Forms;

namespace InputBeacon
{
    internal sealed class FollowLifetime
    {
        private bool initialized, previousUppercase;
        private IntPtr foreground, focus;
        private string confirmedMode, pendingMode;
        private long pendingSince, lastObserved;
        private long expires;
        internal string LastTrigger { get; private set; }
        internal int TriggerCount { get; private set; }

        private static string ModeKey(InputState state)
        {
            if (state.Mode == InputMode.Unknown) return null;
            return state.Mode == InputMode.Other ? "Other:" + state.OtherLabel : state.Mode.ToString();
        }

        private void Trigger(int seconds, long now, string reason)
        {
            expires = now + seconds * 1000L;
            LastTrigger = reason;
            TriggerCount++;
        }

        internal void Observe(InputState state, int seconds, long now)
        {
            // A long polling pause is not continuous evidence of an IME change.
            if (now < lastObserved || now - lastObserved > 400) pendingMode = null;
            lastObserved = now;
            string mode = ModeKey(state);
            if (!initialized || foreground != state.Foreground || focus != state.Focus)
            {
                // A new window/control establishes a baseline. Its different IME
                // context is not evidence of the user switching the input mode.
                initialized = true;
                foreground = state.Foreground;
                focus = state.Focus;
                confirmedMode = mode;
                pendingMode = null;
                previousUppercase = state.Uppercase;
                expires = 0;
                if (LastTrigger == null) LastTrigger = "None";
                return;
            }

            if (previousUppercase != state.Uppercase)
            {
                previousUppercase = state.Uppercase;
                Trigger(seconds, now, "Letter case changed");
            }

            // Unknown is a failed observation, never an input-mode transition.
            // Full-width flags affect painting only, not the follow countdown.
            if (mode == null) { pendingMode = null; return; }
            if (confirmedMode == null) { confirmedMode = mode; pendingMode = null; return; }
            if (mode == confirmedMode) { pendingMode = null; return; }
            if (pendingMode != mode) { pendingMode = mode; pendingSince = now; return; }
            if (now - pendingSince < 200) return;
            confirmedMode = mode;
            pendingMode = null;
            Trigger(seconds, now, "Input mode confirmed");
        }
        internal void Reset() { initialized = false; confirmedMode = pendingMode = null; expires = 0; }
        internal bool ShouldShow(bool enabled, int seconds, bool hasCaret, long now)
        {
            return enabled && hasCaret && (seconds == 0 || now < expires);
        }
    }

    internal sealed class CaretOverlay : Form
    {
        private Bitmap surface;
        private string paintKey;
        private int lastOpacity = -1;
        internal int SurfaceUpdates { get; private set; }
        internal CaretOverlay()
        {
            Text = "键盘状态 · 光标跟随";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
                return value;
            }
        }

        internal static Point Position(Rectangle caret, Size size, Rectangle workArea, int gap)
        {
            int inset = (int)Math.Round(size.Width * 13f / BubblePainter.Width);
            int x = caret.Right + gap - inset;
            if (x + size.Width > workArea.Right) x = caret.Left - size.Width - gap + inset;
            int y = caret.Top - size.Height - gap;
            if (y < workArea.Top) y = caret.Bottom + gap;
            return Settings.Clamp(new Point(x, y), size, workArea);
        }

        internal void UpdateAt(CaretSample caret, InputState state, Settings settings)
        {
            uint dpi = 96;
            try { dpi = Native.GetDpiForWindow(caret.Foreground); } catch (EntryPointNotFoundException) { }
            if (dpi == 0) dpi = 96;
            float scale = dpi / 96f * settings.Scale / 100f;
            Size size = new Size((int)Math.Round(BubblePainter.Width * scale), (int)Math.Round(BubblePainter.Height * scale));
            Rectangle area = Screen.FromRectangle(caret.Bounds).WorkingArea;
            Point location = Position(caret.Bounds, size, area, Math.Max(0, (int)Math.Round(scale)));
            bool tailRight = location.X + size.Width / 2 < caret.Bounds.Left;
            bool below = location.Y >= caret.Bounds.Bottom;
            string key = state.Key + ":" + size + ":" + settings.UseCustomTextColor + ":" + settings.TextColor.ToArgb() + ":" + tailRight + ":" + below;
            bool render = paintKey != key || surface == null;
            if (render)
            {
                Bitmap next = BubblePainter.Render(size, state, settings.UseCustomTextColor ? (Color?)settings.TextColor : null, tailRight, below);
                if (surface != null) surface.Dispose();
                surface = next;
                paintKey = key;
            }
            bool needsPresent = render || !Visible || Location != location || Size != size || lastOpacity != settings.Opacity;
            if (needsPresent)
            {
                SetBounds(location.X, location.Y, size.Width, size.Height);
                LayeredSurface.Present(Handle, location, surface, settings.Opacity);
                lastOpacity = settings.Opacity;
                SurfaceUpdates++;
            }
            if (!Visible) Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && surface != null) { surface.Dispose(); surface = null; }
            base.Dispose(disposing);
        }
    }
}
