using System;
using System.Drawing;
using System.Windows.Forms;

namespace InputBeacon
{
    internal sealed class FollowLifetime
    {
        private string previous;
        private long expires;
        internal void Observe(InputState state, int seconds, long now)
        {
            string key = state.Key;
            if (key != previous) { previous = key; expires = now + seconds * 1000L; }
        }
        internal void Reset() { previous = null; expires = 0; }
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
