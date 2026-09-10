using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace InputBeacon
{
    internal static class JavaIntegrationTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var log = new List<string>();
            string oracle = Path.Combine(args[1], "java-oracle.txt");
            string results = Path.Combine(args[1], "java-results.txt");
            string command = "-Djavax.accessibility.assistive_technologies=com.sun.java.accessibility.AccessBridge -Dsun.java2d.uiScale=1 -cp \"" + args[1] + "\" JavaCaretFixture \"" + oracle + "\"";
            var matched = new HashSet<int>();
            try
            {
                using (var fixture = Process.Start(new ProcessStartInfo(Path.Combine(args[0], "javaw.exe"), command) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                using (var bridge = new JavaCaret())
                {
                    long deadline = CaretTracker.Now + 23000;
                    string previousStatus = null;
                    try
                    {
                        while (!fixture.HasExited && CaretTracker.Now < deadline)
                        {
                            Application.DoEvents();
                            fixture.Refresh();
                            IntPtr window = fixture.MainWindowHandle;
                            Rectangle actual = Rectangle.Empty;
                            long began = CaretTracker.Now;
                            bool read = window != IntPtr.Zero && bridge.TryRead(window, out actual);
                            if (CaretTracker.Now - began > 500) log.Add("Slow bridge: " + (CaretTracker.Now - began));
                            if (read)
                            {
                                try
                                {
                                    string[] fields = File.ReadAllText(oracle).Split(',');
                                    if (fields.Length == 4)
                                    {
                                        int phase = int.Parse(fields[0]), x = int.Parse(fields[1]), y = int.Parse(fields[2]), h = int.Parse(fields[3]);
                                        if (Math.Abs(actual.X - x) <= 2 && Math.Abs(actual.Y - y) <= 2 && Math.Abs(actual.Height - h) <= 3)
                                        {
                                            if (matched.Add(phase)) log.Add("PASS Java phase " + phase + " geometry " + actual);
                                        }
                                        else if (!matched.Contains(phase) && log.Count < 60) log.Add("Mismatch phase " + phase + " actual " + actual + " oracle " + x + "," + y + "," + h);
                                    }
                                }
                                catch (IOException) { }
                                catch (FormatException) { }
                            }
                            if (previousStatus != bridge.Status) { previousStatus = bridge.Status; log.Add("Bridge: " + previousStatus); }
                            File.WriteAllLines(results, log);
                            Thread.Sleep(40);
                        }
                    }
                    finally
                    {
                        if (!fixture.HasExited)
                        {
                            fixture.CloseMainWindow();
                            if (!fixture.WaitForExit(3000))
                                try { fixture.Kill(); } catch (System.ComponentModel.Win32Exception) { log.Add("Fixture was already closing."); }
                        }
                    }
                }
                log.Add("Matched " + matched.Count + "/6 phases (horizontal, newline, end, empty, restored).");
                File.WriteAllLines(results, log);
                return matched.Count == 6 ? 0 : 1;
            }
            catch (Exception error) { log.Add(error.ToString()); File.WriteAllLines(results, log); return 1; }
        }
    }
}
