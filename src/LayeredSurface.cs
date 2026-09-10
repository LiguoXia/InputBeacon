using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace InputBeacon
{
    internal static class LayeredSurface
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PointValue { internal int X, Y; internal PointValue(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        private struct SizeValue { internal int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Blend { internal byte Operation, Flags, Opacity, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            internal uint HeaderSize;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, ImageSize;
            internal int XPels, YPels;
            internal uint UsedColors, ImportantColors;
        }

        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref PointValue destination,
            ref SizeValue size, IntPtr sourceDc, ref PointValue source, uint colorKey, ref Blend blend, uint flags);

        internal static void Present(IntPtr window, Point location, Bitmap bitmap, int opacity)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr dib = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                var info = new BitmapInfo
                {
                    HeaderSize = (uint)Marshal.SizeOf(typeof(BitmapInfo)), Width = bitmap.Width, Height = -bitmap.Height,
                    Planes = 1, BitCount = 32, ImageSize = (uint)(bitmap.Width * bitmap.Height * 4)
                };
                IntPtr bits;
                dib = CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int rowBytes = bitmap.Width * 4;
                    var row = new byte[rowBytes];
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, rowBytes);
                        Marshal.Copy(row, 0, IntPtr.Add(bits, y * rowBytes), rowBytes);
                    }
                }
                finally { bitmap.UnlockBits(data); }
                previous = SelectObject(dc, dib);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var destination = new PointValue(location.X, location.Y);
                var source = new PointValue(0, 0);
                var size = new SizeValue { Width = bitmap.Width, Height = bitmap.Height };
                var blend = new Blend { Operation = 0, Flags = 0, Opacity = (byte)Math.Round(opacity * 255 / 100.0), AlphaFormat = 1 };
                if (!UpdateLayeredWindow(window, IntPtr.Zero, ref destination, ref size, dc, ref source, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
                if (dib != IntPtr.Zero) DeleteObject(dib);
                DeleteDC(dc);
            }
        }
    }
}
