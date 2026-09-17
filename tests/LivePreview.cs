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
            using (var form = new Form { Text = "InputBeacon compatibility preview", ClientSize = new Size(760, 400), StartPosition = FormStartPosition.CenterScreen })
            using (var output = new Label { Dock = DockStyle.Fill, Font = new Font("Consolas", 11) })
            using (var tracker = new CaretTracker())
            using (var follower = new CaretOverlay())
            using (var timer = new Timer { Interval = 100 })
            {
                form.Controls.Add(output);
                var continuous = new CheckBox { Text = "持续显示（位置验证）", Checked = true, Dock = DockStyle.Bottom, Height = 32 };
                form.Controls.Add(continuous);
                form.Shown += delegate { Native.SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040); };
                var settings = new Settings { FollowCaret = true, FollowSeconds = 0 };
                var probe = new InputProbe();
                var lifetime = new FollowLifetime();
                string previous = null, recent = "";
                string last = "Switch to a text input to inspect the follow result.";
                timer.Tick += delegate
                {
                    string diagnostic;
                    InputState state = probe.Read(out diagnostic);
                    long now = CaretTracker.Now;
                    lifetime.Observe(state, 1, now);
                    string signature = state.Key + " focus=" + state.Focus;
                    if (signature != previous)
                    {
                        previous = signature;
                        recent = signature + "\r\n" + recent;
                        if (recent.Length > 600) recent = recent.Substring(0, 600);
                    }
                    CaretSample sample = tracker.Read();
                    lifetime.ObserveCaret(state, sample, 1, now);
                    bool timed = lifetime.ShouldShow(true, 1, sample != null, now);
                    if (sample != null && (continuous.Checked || timed))
                    {
                        follower.UpdateAt(sample, state, settings);
                        last = "Last caret=" + sample.Bounds + "\r\nBubble=" + follower.Bounds + "\r\nCompositor updates=" + follower.SurfaceUpdates;
                    }
                    else follower.Hide();
                    output.Text = DateTime.Now.ToString("HH:mm:ss.fff") + "\r\n" + last + "\r\nCurrent caret valid=" + (sample != null) + " Timed=" + timed +
                        "\r\nTriggers=" + lifetime.TriggerCount + " Last=" + lifetime.LastTrigger +
                        "\r\nForeground=" + Native.ClassName(Native.GetForegroundWindow()) + "\r\n" + tracker.Status + "\r\n" + recent;
                };
                timer.Start();
                Application.Run(form);
            }
        }
    }
}
