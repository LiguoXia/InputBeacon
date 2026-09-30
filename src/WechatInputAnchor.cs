using System;
using System.Drawing;

namespace InputBeacon
{
    internal static class WechatInputAnchor
    {
        internal static bool IsEditor(string className, string framework, object controlType,
            object focused, object enabled, object password, object offscreen)
        {
            return className == "mmui::ChatInputField" && framework == "Qt" &&
                Equals(controlType, 50004) && Equals(focused, true) && Equals(enabled, true) &&
                Equals(password, false) && Equals(offscreen, false);
        }

        internal static bool FromBounds(double[] bounds, out Rectangle anchor)
        {
            anchor = Rectangle.Empty;
            if (bounds == null || bounds.Length != 4) return false;
            foreach (double value in bounds)
                if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 100000) return false;
            if (bounds[2] < 20 || bounds[2] > 5000 || bounds[3] < 10 || bounds[3] > 2000) return false;
            // Attach the hint to the editor's upper edge; never call this point
            // the insertion location or add guessed font/margin offsets.
            anchor = new Rectangle((int)Math.Round(bounds[0]), (int)Math.Round(bounds[1]), 1, 1);
            return true;
        }

        internal static bool IsEmptyInsertion(UiaRange document, UiaRange selection)
        {
            return document != null && selection != null && document.CompareEndpoints(0, document, 1) == 0 &&
                selection.CompareEndpoints(0, selection, 1) == 0 && selection.CompareEndpoints(0, document, 0) == 0;
        }
    }
}
