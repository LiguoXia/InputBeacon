using System;
using System.Drawing;
using System.Windows.Forms;

namespace InputBeacon
{
    internal sealed class FollowSettingsForm : Form
    {
        private readonly CheckBox enabled;
        private readonly RadioButton always;
        private readonly NumericUpDown seconds;
        internal bool FollowEnabled { get { return enabled.Checked; } }
        internal int DurationSeconds { get { return always.Checked ? 0 : (int)seconds.Value; } }

        internal FollowSettingsForm(bool follow, int duration)
        {
            Text = "光标跟随设置";
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(394, 239);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 249, 251);
            enabled = new CheckBox { Text = "在输入光标右上方显示状态", Checked = follow, AutoSize = true, Location = new Point(22, 24) };
            var timed = new RadioButton { Text = "切换后", Checked = duration != 0, AutoSize = true, Location = new Point(24, 68) };
            seconds = new NumericUpDown { Minimum = 1, Maximum = 60, Value = duration == 0 ? 3 : Math.Max(1, Math.Min(60, duration)),
                Location = new Point(109, 64), Size = new Size(65, 28), AccessibleName = "消失前秒数" };
            var suffix = new Label { Text = "秒后消失", AutoSize = true, Location = new Point(185, 69) };
            always = new RadioButton { Text = "一直显示，并随输入光标移动", Checked = duration == 0, AutoSize = true, Location = new Point(24, 106) };
            var explanation = new Label { Text = "支持输入、换行和移动光标。\n当前软件不提供光标位置时，提示会自动隐藏。", ForeColor = Color.DimGray, Location = new Point(22, 145), Size = new Size(350, 40) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(198, 196), Size = new Size(82, 30) };
            var apply = new Button { Text = "应用", DialogResult = DialogResult.OK, Location = new Point(290, 196), Size = new Size(82, 30) };
            Controls.AddRange(new Control[] { enabled, timed, seconds, suffix, always, explanation, cancel, apply });
            timed.CheckedChanged += delegate { seconds.Enabled = timed.Checked; };
            seconds.Enabled = timed.Checked;
            AcceptButton = apply;
            CancelButton = cancel;
        }
    }
}
