using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InputBeacon
{
    // Runs against either the current sources or a saved baseline. No user
    // preferences are loaded/saved; no forced GC or working-set trimming.
    internal static class PerformanceTests
    {
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        private static StreamWriter log;



        private sealed class FixtureBox : RichTextBox
        {
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x003d) { DefWndProc(ref message); return; }
                base.WndProc(ref message);
            }
        }

        private static void Measure(string name, int count, Action<int> action)
        {
            for (int i = 0; i < 100; i++) action(i);
            using (Process process = Process.GetCurrentProcess())
            {
                process.Refresh();
                long allocation = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
                long privateBefore = process.PrivateMemorySize64;
                uint gdiBefore = GetGuiResources(process.Handle, 0), userBefore = GetGuiResources(process.Handle, 1);
                int gen0 = GC.CollectionCount(0), gen2 = GC.CollectionCount(2);
                TimeSpan cpu = process.TotalProcessorTime;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < count; i++) action(i);
                watch.Stop();
                process.Refresh();
                log.WriteLine("{0}: count={1}, elapsed_ms={2}, cpu_ms={3:F0}, allocated_bytes={4}, private_before={5}, private_after={6}, working_set={7}, GDI_delta={8}, USER_delta={9}, gen0={10}, gen2={11}",
                    name, count, watch.ElapsedMilliseconds, (process.TotalProcessorTime - cpu).TotalMilliseconds,
                    AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - allocation, privateBefore, process.PrivateMemorySize64,
                    process.WorkingSet64, (long)GetGuiResources(process.Handle, 0) - gdiBefore,
                    (long)GetGuiResources(process.Handle, 1) - userBefore, GC.CollectionCount(0) - gen0, GC.CollectionCount(2) - gen2);
                log.Flush();
                if (GetGuiResources(process.Handle, 0) > gdiBefore + 8 || GetGuiResources(process.Handle, 1) > userBefore + 8)
                    throw new InvalidOperationException(name + " leaked GUI resources");
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            AppDomain.MonitoringIsEnabled = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (log = new StreamWriter(args[0]))
            try
            {
                using (var editor = new Form { ClientSize = new Size(600, 260), StartPosition = FormStartPosition.CenterScreen, Text = "InputBeacon performance fixture" })
                using (var box = new FixtureBox { Location = new Point(25, 90), Size = new Size(540, 130), Text = "InputBeacon performance fixture\nSecond line" })
                using (var follower = new CaretOverlay())
                {
                    editor.Controls.Add(box);
                    editor.Show(); ShowWindow(editor.Handle, 5); editor.Activate(); box.Focus(); box.Select(3, 0);
                    Application.DoEvents();
                    var state = new InputState { Mode = InputMode.English };
                    var settings = new Settings { FollowCaret = true, FollowSeconds = 0 };
                    Point origin = box.PointToScreen(new Point(30, 20));
                    var sample = new CaretSample { Foreground = editor.Handle, Focus = box.Handle, Valid = true, Bounds = new Rectangle(origin.X, origin.Y, 1, 20) };
                    Measure("moving_hint", 2000, delegate(int i) {
                        sample.Bounds = new Rectangle(origin.X + i % 100, origin.Y, 1, 20);
                        follower.UpdateAt(sample, state, settings);
                    });
                    Measure("stationary_hint", 2000, delegate { follower.UpdateAt(sample, state, settings); });
                    follower.Hide();
                    var probe = new InputProbe();
                    MethodInfo read = typeof(InputProbe).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    Func<InputState> poll = read == null ? (Func<InputState>)delegate { string diagnostic; return probe.Read(out diagnostic); } :
                        (Func<InputState>)Delegate.CreateDelegate(typeof(Func<InputState>), probe, read);
                    Measure("input_poll", 20000, delegate { poll(); });
                    IntPtr editorHandle = editor.Handle, boxHandle = box.Handle;
                    Exception failure = null;
                    var worker = new Thread(delegate() {
                        try
                        {
                            using (var reader = new AutomationCaret())
                            {
                                int valid = 0;
                                for (int batch = 0; batch < 5; batch++)
                                    Measure("uia_geometry_batch_" + batch, 1000, delegate {
                                        Rectangle bounds;
                                        if (reader.TryReadControl(editorHandle, boxHandle, out bounds)) valid++;
                                    });
                                log.WriteLine("UIA valid samples=" + valid);
                                if (valid < 5000) throw new InvalidOperationException("Fixture did not provide enough valid UIA caret samples");
#if NATIVE
                                valid = 0;
                                for (int batch = 0; batch < 5; batch++)
                                    Measure("uia_legacy_batch_" + batch, 1000, delegate {
                                        Rectangle bounds;
                                        if (reader.TryReadSelectionControl(editorHandle, boxHandle, out bounds)) valid++;
                                    });
                                log.WriteLine("Legacy UIA valid samples=" + valid);
                                if (valid < 5000) throw new InvalidOperationException("Legacy UIA stress did not return enough valid carets");
#endif
                            }
                        }
                        catch (Exception error) { failure = error; }
                        finally { editor.BeginInvoke((Action)delegate { Application.ExitThread(); }); }
                    });
                    worker.SetApartmentState(ApartmentState.MTA); worker.IsBackground = true; worker.Start();
                    using (var deadline = new System.Windows.Forms.Timer { Interval = 180000 })
                    {
                        deadline.Tick += delegate { Application.ExitThread(); };
                        deadline.Start();
                        // A real message loop services synchronous UIA callbacks
                        // immediately; DoEvents + Sleep would measure the sleep.
                        Application.Run();
                    }
                    if (!worker.Join(1000)) throw new TimeoutException("UIA fixture timed out");
                    if (failure != null) throw failure;
                }
                log.WriteLine("PASS"); return 0;
            }
            catch (Exception error) { log.WriteLine(error); return 1; }
        }
    }
}
