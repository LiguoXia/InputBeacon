using System;
using System.Drawing;
using System.Windows.Forms;

namespace InputBeacon
{
    internal sealed class ColorSettingsForm : Form
    {
        private readonly CheckBox custom;
        private readonly TextBox hex;
        private readonly Label validation;
        private readonly Panel preview;
        private readonly Button apply;
        private Color selected;
        internal bool UseCustomColor { get { return custom.Checked; } }
        internal Color SelectedColor { get { return selected; } }

        internal ColorSettingsForm(bool useCustomColor, Color initialColor)
        {
            selected = initialColor;
            Text = "文字颜色";
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(436, 358);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 249, 251);

            Controls.Add(new Label { Text = "选择适合你屏幕的文字颜色", AutoSize = true, Location = new Point(22, 19) });
            custom = new CheckBox { Text = "使用自定义颜色", AutoSize = true, Checked = useCustomColor, Location = new Point(22, 51) };
            Controls.Add(custom);
            string[] swatches = { "#0071E3", "#FFFFFF", "#202124", "#8B5CF6", "#10B981", "#F97316", "#EF4444", "#64748B" };
            for (int i = 0; i < swatches.Length; i++)
            {
                Color color;
                ColorValue.TryParse(swatches[i], out color);
                Color choice = color;
                var button = new Button { Location = new Point(22 + i * 39, 87), Size = new Size(30, 30), BackColor = color,
                    FlatStyle = FlatStyle.Flat, AccessibleName = swatches[i], UseVisualStyleBackColor = false };
                button.FlatAppearance.BorderColor = Color.FromArgb(207, 212, 219);
                button.Click += delegate { PickColor(choice); };
                Controls.Add(button);
            }
            var palette = new Button { Text = "调色盘…", Location = new Point(340, 87), Size = new Size(76, 30) };
            palette.Click += delegate
            {
                using (var picker = new ColorDialog { Color = selected, FullOpen = true, AnyColor = true })
                    if (picker.ShowDialog(this) == DialogResult.OK) PickColor(picker.Color);
            };
            Controls.Add(palette);
            Controls.Add(new Label { Text = "HEX", AutoSize = true, Location = new Point(23, 138) });
            hex = new TextBox { Text = ColorValue.Hex(selected), Location = new Point(66, 133), Size = new Size(144, 27),
                Font = new Font("Consolas", 11), MaxLength = 16, AccessibleName = "HEX 颜色" };
            Controls.Add(hex);
            Controls.Add(new Label { Text = "如 #0071E3 或 #FFF", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(224, 138) });
            validation = new Label { AutoSize = false, Location = new Point(22, 170), Size = new Size(394, 23), ForeColor = Color.FromArgb(196, 59, 48) };
            Controls.Add(validation);
            preview = new Panel { Location = new Point(22, 200), Size = new Size(394, 92) };
            preview.Paint += PaintPreview;
            Controls.Add(preview);
            var restore = new Button { Text = "恢复默认配色", Location = new Point(22, 313), Size = new Size(112, 30) };
            restore.Click += delegate { custom.Checked = false; };
            Controls.Add(restore);
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(238, 313), Size = new Size(82, 30) };
            Controls.Add(cancel);
            apply = new Button { Text = "应用", Location = new Point(330, 313), Size = new Size(86, 30) };
            apply.Click += delegate { if (ValidateColor()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(apply);
            AcceptButton = apply;
            CancelButton = cancel;
            hex.TextChanged += delegate { custom.Checked = true; ValidateColor(); };
            custom.CheckedChanged += delegate { ValidateColor(); };
            ValidateColor();
        }

        private void PickColor(Color color) { selected = color; custom.Checked = true; hex.Text = ColorValue.Hex(color); ValidateColor(); }

        private bool ValidateColor()
        {
            Color parsed;
            bool valid = ColorValue.TryParse(hex.Text, out parsed);
            if (valid) selected = parsed;
            validation.Text = custom.Checked && !valid ? "请输入 3 位或 6 位 HEX 色值，例如 #0071E3。" : "";
            apply.Enabled = !custom.Checked || valid;
            preview.Invalidate();
            return apply.Enabled;
        }

        private void PaintPreview(object sender, PaintEventArgs e)
        {
            int half = preview.Width / 2;
            e.Graphics.Clear(Color.White);
            using (var dark = new SolidBrush(Color.FromArgb(28, 30, 35))) e.Graphics.FillRectangle(dark, half, 0, preview.Width - half, preview.Height);
            e.Graphics.DrawString("浅色背景", Font, Brushes.Gray, 12, 9);
            e.Graphics.DrawString("深色背景", Font, Brushes.LightGray, half + 12, 9);
            float scale = preview.DeviceDpi / 96f;
            using (Bitmap image = CardPainter.Render(new Size((int)(84 * scale), (int)(36 * scale)),
                new InputState { Mode = InputMode.Chinese, Caps = true }, custom.Checked ? (Color?)selected : null))
            {
                e.Graphics.DrawImageUnscaled(image, (half - image.Width) / 2, (preview.Height - image.Height) / 2 + 8);
                e.Graphics.DrawImageUnscaled(image, half + (half - image.Width) / 2, (preview.Height - image.Height) / 2 + 8);
            }
        }
    }
}
