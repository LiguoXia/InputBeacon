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
        internal string InputIdentity;
    }

    internal sealed class CaretTracker : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly AutoResetEvent javaWake = new AutoResetEvent(false);
        private readonly Thread worker;
        private readonly Thread javaWorker;
        private CaretSample latest, javaLatest;
        private string lastStatus = "Waiting for an input caret";
        private string lastJavaStatus = "Java bridge has not been requested";
        internal string Status { get { lock (gate) return lastStatus + "\r\n" + lastJavaStatus; } }
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
            worker = new Thread(delegate() { Work(false); }) { IsBackground = true, Name = "InputBeacon Windows caret" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            javaWorker = new Thread(delegate() { Work(true); }) { IsBackground = true, Name = "InputBeacon Java caret" };
            javaWorker.SetApartmentState(ApartmentState.STA);
            javaWorker.Start();
        }

        internal static long Now { get { return (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency)); } }

        internal CaretSample Read()
        {
            IntPtr foreground = Native.GetForegroundWindow();
            uint processId;
            uint threadId = Native.GetWindowThreadProcessId(foreground, out processId);
            if (foreground == IntPtr.Zero || threadId == 0 || processId == ownProcess) return null;
            var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf(typeof(Native.GuiThreadInfo)) };
            bool hasThreadInfo = Native.GetGUIThreadInfo(threadId, ref info);
            if (hasThreadInfo && (info.Flags & 0x1e) != 0) return null;
            IntPtr focus = hasThreadInfo && info.Focus != IntPtr.Zero ? info.Focus : foreground;
            Rectangle native;
            bool nativeFound = TryNative(info, out native) && IsInsideWindow(foreground, native);
            lock (gate)
            {
                if (stopping) return null;
                requestedForeground = foreground;
                requestedFocus = focus;
                wake.Set();
                if (Native.ClassName(foreground).StartsWith("SunAwt", StringComparison.Ordinal)) javaWake.Set();
                if (nativeFound)
                {
                    lastStatus = "Win32 caret";
                    return Native.GetForegroundWindow() == foreground ? new CaretSample {
                        Foreground = foreground, Focus = focus, Bounds = native, Timestamp = Now, Valid = true,
                        InputIdentity = Fresh(latest, foreground, focus, Now) ? latest.InputIdentity : null
                    } : null;
                }
                if (Fresh(javaLatest, foreground, focus, Now)) return javaLatest;
                if (Fresh(latest, foreground, focus, Now))
                    return latest;
            }
            return null;
        }

        internal static bool Fresh(CaretSample sample, IntPtr foreground, IntPtr focus, long now)
        {
            return sample != null && sample.Valid && sample.Foreground == foreground && sample.Focus == focus && now >= sample.Timestamp && now - sample.Timestamp < 350;
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

        private void Work(bool java)
        {
            var automation = java ? null : new AutomationCaret();
            var bridge = java ? new JavaCaret() : null;
            AutoResetEvent signal = java ? javaWake : wake;
            try
            {
                while (!stopping)
                {
                    bool requested = signal.WaitOne(java ? 25 : Timeout.Infinite);
                    // Windows_run owns a message window on this STA. Its discovery
                    // and Java callbacks require a pump even between sample requests.
                    if (java) System.Windows.Forms.Application.DoEvents();
                    if (stopping) break;
                    if (!requested) continue;
                    IntPtr foreground, focus;
                    lock (gate) { foreground = requestedForeground; focus = requestedFocus; }
                    var sample = new CaretSample { Foreground = foreground, Focus = focus, Timestamp = Now };
                    string status = "No caret in the active control";
                    try
                    {
                        Rectangle rectangle;
                        bool found = false;
                        rectangle = Rectangle.Empty;
                        if (Native.GetForegroundWindow() == foreground)
                        {
                            if (java) { found = bridge.TryRead(foreground, out rectangle); status = bridge.Status; }
                            else
                            {
                                uint ignored;
                                var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf(typeof(Native.GuiThreadInfo)) };
                                found = Native.GetGUIThreadInfo(Native.GetWindowThreadProcessId(foreground, out ignored), ref info) &&
                                    TryNative(info, out rectangle) && IsInsideWindow(foreground, rectangle);
                                if (found) status = "Win32 caret";
                                else
                                {
                                    found = (TryAccessibleCaret(focus, out rectangle) && IsInsideWindow(foreground, rectangle)) ||
                                        (TryAccessibleCaret(foreground, out rectangle) && IsInsideWindow(foreground, rectangle)) ||
                                        (TryAccessibleCaret(IntPtr.Zero, out rectangle) && IsInsideWindow(foreground, rectangle));
                                    if (found) status = "MSAA caret";
                                    else
                                    {
                                        found = automation.TryRead(foreground, out rectangle);
                                        status = automation.Status;
                                        if (!found && TryAutomation(foreground, out rectangle)) { found = true; status = "UIA TextPattern"; }
                                    }
                                }
                            }
                        }
                        if (found)
                        {
                            sample.Bounds = rectangle;
                            sample.Valid = Native.GetForegroundWindow() == foreground && IsInsideWindow(foreground, rectangle);
                            sample.Timestamp = Now;
                            // UIA runtime IDs distinguish virtual fields that share an HWND.
                            // Keep this on the existing worker; never read input text.
                            if (!java && sample.Valid) sample.InputIdentity = automation.ReadFocusIdentity(foreground);
                            sample.Valid &= Native.GetForegroundWindow() == foreground;
                        }
                    }
                    catch (COMException) { }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                    catch (ArgumentException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (NotSupportedException) { }
                    catch (NotImplementedException) { }
                    catch (System.Security.SecurityException) { }
                    // Age starts at geometry acquisition; a slow identity query
                    // must not make old geometry appear fresh.
                    lock (gate)
                    {
                        if (java) { javaLatest = sample; lastJavaStatus = status ?? "Java caret unavailable"; }
                        else { latest = sample; lastStatus = status ?? "Windows caret unavailable"; }
                    }
                }
            }
            finally { if (bridge != null) bridge.Dispose(); if (automation != null) automation.Dispose(); signal.Dispose(); }
        }

        internal static bool TryAccessibleCaret(IntPtr window, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
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
            catch (NotImplementedException) { return false; }
            catch (NotSupportedException) { return false; }
            finally { if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
        }

        // This reads geometry only: no Name, Value, selected text, or GetText calls.
        // Providers run on a single background thread so an unresponsive application
        // cannot block the indicator or accumulate parallel automation requests.
        internal static bool TryAutomation(IntPtr foreground, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            AutomationElement element = AutomationElement.FocusedElement;
            if (element == null || !element.Current.HasKeyboardFocus || element.Current.IsOffscreen || !BelongsToForeground(element, foreground)) return false;
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

        private static bool BelongsToForeground(AutomationElement element, IntPtr foreground)
        {
            for (int depth = 0; element != null && depth < 32; depth++)
            {
                IntPtr window = new IntPtr(element.Current.NativeWindowHandle);
                if (window == foreground || (window != IntPtr.Zero && Native.IsChild(foreground, window))) return true;
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return false;
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
            lock (gate) { if (stopping) return; stopping = true; wake.Set(); javaWake.Set(); }
            // Do not wait on a remote accessibility provider during shutdown.
        }
    }
}
