using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace InputBeacon
{
    internal static class CardPainter
    {
        internal const int Width = 84, Height = 36;
        private const int Supersampling = 4;

        internal static Bitmap RenderTaskbarIcon(InputState state, bool letterCase, Color? customColor)
        {
            using (var large = new Bitmap(128, 128, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(large))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.ScaleTransform(4, 4);
                    string symbol = letterCase ? (state.Uppercase ? "A" : "a") : state.ModeSymbol;
                    Color color = customColor ?? (letterCase && state.Uppercase ? Color.FromArgb(193, 105, 0) :
                        (!letterCase && state.Mode == InputMode.Unknown ? Color.FromArgb(130, 137, 148) : ColorValue.Default));
                    DrawGlyph(graphics, symbol, letterCase ? "Segoe UI" : "Microsoft YaHei UI", letterCase ? 28 : 25,
                        FontStyle.Regular, new RectangleF(1, 1, 30, 29), color);
                    if (letterCase ? state.Caps : state.FullWidth)
                        using (var fill = new SolidBrush(color)) graphics.FillEllipse(fill, 14.5f, 29, 3, 2);
                }
                var result = new Bitmap(32, 32, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(large, new Rectangle(0, 0, 32, 32), 0, 0, 128, 128, GraphicsUnit.Pixel);
                }
                return result;
            }
        }

        // Paint vector glyphs at four times the physical resolution, then reduce
        // onto a premultiplied alpha surface. No background, border, or region mask.
        internal static Bitmap Render(Size size, InputState state, Color? customColor = null)
        {
            using (var large = new Bitmap(size.Width * Supersampling, size.Height * Supersampling, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(large))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.ScaleTransform((float)large.Width / Width, (float)large.Height / Height);
                    DrawGlyph(graphics, state.ModeSymbol, "Microsoft YaHei UI", 18, FontStyle.Regular,
                        new RectangleF(8, 7, 26, 23), customColor ?? (state.Mode == InputMode.Unknown ? Color.FromArgb(130, 137, 148) : ColorValue.Default));
                    DrawGlyph(graphics, state.Uppercase ? "A" : "a", "Segoe UI", 21, FontStyle.Regular,
                        new RectangleF(50, 7, 26, 23), customColor ?? (state.Uppercase ? Color.FromArgb(193, 105, 0) : ColorValue.Default));
                    using (var separator = new Pen(Color.FromArgb(110, customColor ?? Color.FromArgb(138, 146, 157)), 0.7f))
                    {
                        separator.StartCap = separator.EndCap = LineCap.Round;
                        graphics.DrawLine(separator, 42, 13, 42, 24);
                    }
                    // The small Caps dot preserves the lock distinction when Shift reverses case.
                    if (state.Caps)
                        using (var brush = new SolidBrush(Color.FromArgb(210, customColor ?? Color.FromArgb(193, 105, 0)))) graphics.FillEllipse(brush, 61.5f, 31, 3, 3);
                    if (state.Shift)
                        DrawGlyph(graphics, "↑", "Segoe UI", 8, FontStyle.Regular, new RectangleF(73, 4, 7, 10), customColor ?? Color.FromArgb(130, 137, 148));
                    if (state.FullWidth)
                        using (var brush = new SolidBrush(Color.FromArgb(210, customColor ?? Color.FromArgb(193, 105, 0)))) graphics.FillEllipse(brush, 19.5f, 31, 3, 3);
                }
                var result = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(result))
                using (var attributes = new ImageAttributes())
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(large, new Rectangle(Point.Empty, size), 0, 0, large.Width, large.Height, GraphicsUnit.Pixel, attributes);
                }
                return result;
            }
        }

        private static void DrawGlyph(Graphics graphics, string text, string familyName, float emSize,
            FontStyle style, RectangleF bounds, Color color)
        {
            using (var family = new FontFamily(familyName))
            using (var path = new GraphicsPath())
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                path.AddString(text, family, (int)style, emSize, PointF.Empty, format);
                RectangleF ink = path.GetBounds();
                using (var transform = new Matrix())
                {
                    transform.Translate(bounds.Left + (bounds.Width - ink.Width) / 2 - ink.Left,
                        bounds.Top + (bounds.Height - ink.Height) / 2 - ink.Top);
                    path.Transform(transform);
                }
                // A fine soft light rim keeps blue/orange glyphs legible over dark terminals.
                // It follows glyph contours only; the rest of the window stays alpha = 0.
                Color rim = color.GetBrightness() > 0.8f ? Color.FromArgb(85, 40, 45, 52) : Color.FromArgb(75, 255, 255, 255);
                using (var halo = new Pen(rim, 1.6f) { LineJoin = LineJoin.Round })
                    graphics.DrawPath(halo, path);
                using (var fill = new SolidBrush(color)) graphics.FillPath(fill, path);
            }
        }
    }
}
