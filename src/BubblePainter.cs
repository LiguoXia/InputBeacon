using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace InputBeacon
{
    internal static class BubblePainter
    {
        internal const int Width = 72, Height = 34;
        internal static Bitmap Render(Size size, InputState state, Color? customColor, bool tailRight, bool below)
        {
            using (var large = new Bitmap(size.Width * 4, size.Height * 4, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(large))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.ScaleTransform(large.Width / (float)Width, large.Height / (float)Height);
                    using (GraphicsPath shape = Outline(tailRight, below))
                    {
                        using (var shadow = new Pen(Color.FromArgb(13, 45, 52, 65), 3)) g.DrawPath(shadow, shape);
                        using (var shadow = new Pen(Color.FromArgb(17, 45, 52, 65), 1.7f)) g.DrawPath(shadow, shape);
                        using (var glass = new SolidBrush(Color.FromArgb(232, 250, 251, 253))) g.FillPath(glass, shape);
                        using (var edge = new Pen(Color.FromArgb(200, 255, 255, 255), 0.7f)) g.DrawPath(edge, shape);
                    }
                    float dy = below ? 6 : 0;
                    Color language = customColor ?? (state.Mode == InputMode.Unknown ? Color.FromArgb(130, 137, 148) : ColorValue.Default);
                    Color letter = customColor ?? (state.Uppercase ? Color.FromArgb(193, 105, 0) : ColorValue.Default);
                    CardPainter.DrawGlyph(g, state.ModeSymbol, "Microsoft YaHei UI", 17, FontStyle.Regular, new RectangleF(6, 3 + dy, 24, 21), language);
                    CardPainter.DrawGlyph(g, state.Uppercase ? "A" : "a", "Segoe UI", 19, FontStyle.Regular, new RectangleF(42, 3 + dy, 22, 21), letter);
                    using (var separator = new Pen(Color.FromArgb(65, customColor ?? Color.FromArgb(95, 104, 120)), 0.7f)) g.DrawLine(separator, 36, 9 + dy, 36, 20 + dy);
                    if (state.Caps) using (var fill = new SolidBrush(letter)) g.FillEllipse(fill, 52, 24 + dy, 2.4f, 2.4f);
                    if (state.FullWidth) using (var fill = new SolidBrush(language)) g.FillEllipse(fill, 17, 24 + dy, 2.4f, 2.4f);
                    if (state.Shift) CardPainter.DrawGlyph(g, "↑", "Segoe UI", 7, FontStyle.Regular, new RectangleF(63, 2 + dy, 6, 8), letter);
                }
                var result = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(result))
                using (var attributes = new ImageAttributes())
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(large, new Rectangle(Point.Empty, size), 0, 0, large.Width, large.Height, GraphicsUnit.Pixel, attributes);
                }
                return result;
            }
        }

        private static GraphicsPath Outline(bool right, bool below)
        {
            float tip = right ? 59 : 13;
            var path = new GraphicsPath();
            path.AddArc(2, 1, 16, 16, 180, 90);
            path.AddLine(10, 1, 62, 1);
            path.AddArc(54, 1, 16, 16, 270, 90);
            path.AddLine(70, 9, 70, 19);
            path.AddArc(54, 11, 16, 16, 0, 90);
            path.AddLine(62, 27, tip + 3, 27);
            path.AddLine(tip + 3, 27, tip, 31);
            path.AddLine(tip, 31, tip - 3, 27);
            path.AddLine(tip - 3, 27, 10, 27);
            path.AddArc(2, 11, 16, 16, 90, 90);
            path.CloseFigure();
            if (below)
                using (var transform = new Matrix(1, 0, 0, -1, 0, Height)) path.Transform(transform);
            return path;
        }
    }
}
