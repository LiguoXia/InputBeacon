using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Accessibility;

namespace InputBeacon
{
    internal sealed class CaretSample
    {
        internal IntPtr Foreground, Focus;
        internal Rectangle Bounds;
        internal long Timestamp;
        internal bool Valid;
    }

    internal sealed class CaretTracker : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread worker;
        private CaretSample latest;
        private IntPtr requestedForeground, requestedFocus;
        private volatile bool stopping;
        private readonly int ownProcess = Process.GetCurrentProcess().Id;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int X, Y; }
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Native.Rect rectangle);
        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);

        internal CaretTracker()
        {
            worker = new Thread(Work) { IsBackground = true, Name = "InputBeacon caret geometry" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        internal static long Now { get { return (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency)); } }

        internal CaretSample Read()
        {
            IntPtr foreground = Native.GetForegroundWindow();
            uint processId;
            uint threadId = Native.GetWindowThreadProcessId(foreground, out processId);
            if (foreground == IntPtr.Zero || threadId == 0 || processId == ownProcess) return null;
            var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf(typeof(Native.GuiThreadInfo)) };
            if (!Native.GetGUIThreadInfo(threadId, ref info) || (info.Flags & 0x1e) != 0) return null;
            IntPtr focus = info.Focus;
            Rectangle native;
            if (TryNative(info, out native) && IsInsideWindow(foreground, native))
                return Native.GetForegroundWindow() == foreground ? new CaretSample { Foreground = foreground, Focus = focus, Bounds = native, Timestamp = Now, Valid = true } : null;
            lock (gate)
            {
                if (stopping) return null;
                requestedForeground = foreground;
                requestedFocus = focus;
                wake.Set();
                if (latest != null && latest.Valid && latest.Foreground == foreground && latest.Focus == focus && Now - latest.Timestamp < 350)
                    return latest;
            }
            return null;
        }

        internal static bool TryNative(Native.GuiThreadInfo info, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (info.Caret == IntPtr.Zero || !Native.IsWindowVisible(info.Caret)) return false;
            var origin = new NativePoint { X = info.CaretRect.Left, Y = info.CaretRect.Top };
            var end = new NativePoint { X = info.CaretRect.Right, Y = info.CaretRect.Bottom };
            if (!ClientToScreen(info.Caret, ref origin) || !ClientToScreen(info.Caret, ref end)) return false;
            rectangle = Rectangle.FromLTRB(origin.X, origin.Y, Math.Max(origin.X + 1, end.X), end.Y);
            return IsCaretRectangle(rectangle);
        }

        internal static bool IsCaretRectangle(Rectangle rectangle)
        {
            return rectangle.Height > 0 && rectangle.Height < 300 && rectangle.Width >= 0 && rectangle.Width < 200 &&
                rectangle.Left > -100000 && rectangle.Left < 100000 && rectangle.Top > -100000 && rectangle.Top < 100000;
        }

        private static bool IsInsideWindow(IntPtr window, Rectangle caret)
        {
            Native.Rect bounds;
            return GetWindowRect(window, out bounds) && Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom).IntersectsWith(caret);
        }

        private void Work()
        {
            try
            {
                while (!stopping)
                {
                    wake.WaitOne();
                    if (stopping) break;
                    IntPtr foreground, focus;
                    lock (gate) { foreground = requestedForeground; focus = requestedFocus; }
                    var sample = new CaretSample { Foreground = foreground, Focus = focus, Timestamp = Now };
                    try
                    {
                        Rectangle rectangle;
                        if (Native.GetForegroundWindow() == foreground &&
                            (TryAccessibleCaret(focus, out rectangle) || TryAccessibleCaret(foreground, out rectangle) || TryAutomation(foreground, out rectangle)))
                        {
                            sample.Bounds = rectangle;
                            sample.Valid = Native.GetForegroundWindow() == foreground && IsInsideWindow(foreground, rectangle);
                        }
                    }
                    catch (COMException) { }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                    catch (ArgumentException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (NotSupportedException) { }
                    catch (System.Security.SecurityException) { }
                    lock (gate) latest = sample;
                }
            }
            finally { wake.Dispose(); }
        }

        private static bool TryAccessibleCaret(IntPtr window, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (window == IntPtr.Zero) return false;
            IAccessible accessible = null;
            try
            {
                var iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
                if (AccessibleObjectFromWindow(window, unchecked((uint)-8), ref iid, out accessible) != 0 || accessible == null) return false;
                int state = Convert.ToInt32(accessible.get_accState(0));
                if ((state & 0x18000) != 0) return false;
                int left, top, width, height;
                accessible.accLocation(out left, out top, out width, out height, 0);
                rectangle = new Rectangle(left, top, Math.Max(1, width), height);
                return IsCaretRectangle(rectangle);
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
            catch (FormatException) { return false; }
            finally { if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
        }

        // This reads geometry only: no Name, Value, selected text, or GetText calls.
        // Providers run on a single background thread so an unresponsive application
        // cannot block the indicator or accumulate parallel automation requests.
        internal static bool TryAutomation(IntPtr foreground, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            AutomationElement element = AutomationElement.FocusedElement;
            uint processId;
            Native.GetWindowThreadProcessId(foreground, out processId);
            if (element == null || element.Current.ProcessId != processId || element.Current.IsOffscreen) return false;
            object patternObject;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out patternObject)) return false;
            TextPatternRange[] ranges = ((TextPattern)patternObject).GetSelection();
            if (ranges == null || ranges.Length != 1) return false;
            TextPatternRange caret = ranges[0];
            if (caret.CompareEndpoints(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End) != 0) return false;
            System.Windows.Rect[] bounds = caret.GetBoundingRectangles();
            if (bounds.Length == 1 && bounds[0].Height > 0)
            {
                rectangle = ConvertBounds(bounds[0], false);
                return IsCaretRectangle(rectangle);
            }
            TextPatternRange character = caret.Clone();
            character.ExpandToEnclosingUnit(TextUnit.Character);
            bounds = character.GetBoundingRectangles();
            if (bounds.Length != 1 || bounds[0].Height <= 0) return false;
            bool atEnd = caret.CompareEndpoints(TextPatternRangeEndpoint.Start, character, TextPatternRangeEndpoint.End) == 0;
            rectangle = ConvertBounds(bounds[0], atEnd);
            return IsCaretRectangle(rectangle);
        }

        private static Rectangle ConvertBounds(System.Windows.Rect bounds, bool atEnd)
        {
            if (bounds.IsEmpty || double.IsNaN(bounds.X) || double.IsInfinity(bounds.X) ||
                double.IsNaN(bounds.Y) || double.IsInfinity(bounds.Y) || double.IsNaN(bounds.Height) || double.IsInfinity(bounds.Height) ||
                double.IsNaN(bounds.Width) || double.IsInfinity(bounds.Width) || bounds.X < -100000 || bounds.Right > 100000 ||
                bounds.Y < -100000 || bounds.Bottom > 100000) return Rectangle.Empty;
            return new Rectangle((int)Math.Round(atEnd ? bounds.Right : bounds.Left), (int)Math.Round(bounds.Top), 1, (int)Math.Ceiling(bounds.Height));
        }

        public void Dispose()
        {
            lock (gate) { if (stopping) return; stopping = true; wake.Set(); }
            // Do not wait on a remote accessibility provider during shutdown.
        }
    }
}
