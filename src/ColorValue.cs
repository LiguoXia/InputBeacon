using System;
using System.Drawing;
using System.Globalization;

namespace InputBeacon
{
    internal static class ColorValue
    {
        internal static readonly Color Default = Color.FromArgb(0, 113, 227);

        internal static bool TryParse(string text, out Color color)
        {
            color = Default;
            if (text == null) return false;
            string hex = text.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal)) hex = hex.Substring(1);
            if (hex.Length == 3)
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            uint rgb;
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb)) return false;
            color = Color.FromArgb((int)((rgb >> 16) & 255), (int)((rgb >> 8) & 255), (int)(rgb & 255));
            return true;
        }

        internal static string Hex(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
    }
}
