using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
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
        private Thread javaWorker;
        private CaretSample latest, javaLatest;
        private string lastStatus = "Waiting for an input caret";
        private string lastJavaStatus = "Java bridge has not been requested";
        internal string Status { get { lock (gate) return lastStatus + "\r\n" + lastJavaStatus; } }
        private IntPtr requestedForeground, requestedFocus;
        private volatile bool stopping;
        private readonly int ownProcess = CurrentProcessId();

        private static int CurrentProcessId()
        {
            using (Process process = Process.GetCurrentProcess()) return process.Id;
        }

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
                if (Native.ClassName(foreground).StartsWith("SunAwt", StringComparison.Ordinal))
                {
                    if (javaWorker == null)
                    {
                        javaWorker = new Thread(delegate() { Work(true); }) { IsBackground = true, Name = "InputBeacon Java caret" };
                        javaWorker.SetApartmentState(ApartmentState.STA);
                        javaWorker.Start();
                    }
                    javaWake.Set();
                }
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
                    bool requested = signal.WaitOne(java ? (bridge.IsInitialized ? 25 : Timeout.Infinite) : 30000);
                    // Windows_run owns a message window on this STA. Its discovery
                    // and Java callbacks require a pump even between sample requests.
                    if (java && bridge.IsInitialized) System.Windows.Forms.Application.DoEvents();
                    if (stopping) break;
                    if (!requested)
                    {
                        // Release the UIA connection after following has been idle
                        // for 30 seconds, while keeping the single worker reusable.
                        if (automation != null) automation.Dispose();
                        continue;
                    }
                    IntPtr foreground, focus;
                    lock (gate) { foreground = requestedForeground; focus = requestedFocus; }
                    var sample = new CaretSample { Foreground = foreground, Focus = focus, Timestamp = Now };
                    string status = "No caret in the active control";
                    try
                    {
                        Rectangle rectangle;
                        bool found = false;
                        long geometryTimestamp = 0;
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
                                        if (found)
                                        {
                                            sample.InputIdentity = automation.InputIdentity;
                                            geometryTimestamp = automation.GeometryTimestamp;
                                        }
                                    }
                                }
                            }
                        }
                        if (found)
                        {
                            sample.Bounds = rectangle;
                            sample.Valid = Native.GetForegroundWindow() == foreground && IsInsideWindow(foreground, rectangle);
                            sample.Timestamp = geometryTimestamp == 0 ? Now : geometryTimestamp;
                            // UIA runtime IDs distinguish virtual fields that share an HWND.
                            // Keep this on the existing worker; never read input text.
                            if (!java && sample.Valid && sample.InputIdentity == null) sample.InputIdentity = automation.ReadFocusIdentity(foreground);
                            sample.Valid &= Native.GetForegroundWindow() == foreground;
                        }
                    }
                    catch (COMException) { }
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

        public void Dispose()
        {
            lock (gate)
            {
                if (stopping) return;
                stopping = true;
                wake.Set();
                if (javaWorker != null) javaWake.Set(); else javaWake.Dispose();
            }
            // Do not wait on a remote accessibility provider during shutdown.
        }
    }
}
