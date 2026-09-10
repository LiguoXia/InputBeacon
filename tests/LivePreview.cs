using System;
using System.Drawing;
using System.Windows.Forms;

namespace InputBeacon
{
    // Interactive QA window for the actual tracker and compositor. Geometry only.
    internal static class LivePreview
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            using (var form = new Form { Text = "InputBeacon compatibility preview", ClientSize = new Size(630, 250), StartPosition = FormStartPosition.CenterScreen })
            using (var output = new Label { Dock = DockStyle.Fill, Font = new Font("Consolas", 11) })
            using (var tracker = new CaretTracker())
            using (var follower = new CaretOverlay())
            using (var timer = new Timer { Interval = 100 })
            {
                form.Controls.Add(output);
                form.Shown += delegate { Native.SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040); };
                var settings = new Settings { FollowCaret = true, FollowSeconds = 0 };
                var probe = new InputProbe();
                string last = "Switch to a text input to inspect the follow result.";
                timer.Tick += delegate
                {
                    CaretSample sample = tracker.Read();
                    if (sample != null)
                    {
                        string diagnostic;
                        follower.UpdateAt(sample, probe.Read(out diagnostic), settings);
                        last = "Caret=" + sample.Bounds + "\r\nBubble=" + follower.Bounds + "\r\nCompositor updates=" + follower.SurfaceUpdates;
                    }
                    else follower.Hide();
                    output.Text = last + "\r\nCurrent caret valid=" + (sample != null) + "\r\nForeground=" + Native.ClassName(Native.GetForegroundWindow()) + "\r\n" + tracker.Status;
                };
                timer.Start();
                Application.Run(form);
            }
        }
    }
}
