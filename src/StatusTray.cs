using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace InputBeacon
{
    // Native notification-area slots: Windows handles positioning and reserves
    // space for each glyph, so the indicator never covers taskbar controls.
    internal sealed class StatusTray : IDisposable
    {
        private readonly NotifyIcon language;
        private readonly NotifyIcon letterCase;
        private readonly Icon appIcon;
        private Icon languageIcon, caseIcon;
        internal bool StatusVisible { get; private set; }
        internal bool ControlIconVisible { get { return language.Visible; } }
        internal bool CaseIconVisible { get { return letterCase.Visible; } }

        internal StatusTray(ContextMenuStrip menu, Action toggleFloating)
        {
            appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
            language = new NotifyIcon { Icon = appIcon, Text = "键盘状态", ContextMenuStrip = menu, Visible = true };
            letterCase = new NotifyIcon { Text = "字母大小写", ContextMenuStrip = menu };
            MouseEventHandler toggle = delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) toggleFloating(); };
            language.MouseDoubleClick += toggle;
            letterCase.MouseDoubleClick += toggle;
        }

        internal void Update(InputState state, Settings settings)
        {
            StatusVisible = settings.ShowTaskbarStatus;
            language.Text = "键盘状态 | " + state.ModeTitle + " | " + state.CaseTitle + " | " + state.CaseDetail;
            letterCase.Text = "字母" + state.CaseTitle + " | " + state.CaseDetail;
            if (StatusVisible)
            {
                Color? custom = settings.UseCustomTextColor ? (Color?)settings.TextColor : null;
                Icon nextLanguage = CreateIcon(state, false, custom);
                Icon nextCase;
                try { nextCase = CreateIcon(state, true, custom); }
                catch { nextLanguage.Dispose(); throw; }
                language.Icon = nextLanguage;
                letterCase.Icon = nextCase;
                if (languageIcon != null) languageIcon.Dispose();
                if (caseIcon != null) caseIcon.Dispose();
                languageIcon = nextLanguage;
                caseIcon = nextCase;
            }
            else
            {
                letterCase.Visible = false;
                language.Icon = appIcon;
                letterCase.Icon = null;
                if (languageIcon != null) { languageIcon.Dispose(); languageIcon = null; }
                if (caseIcon != null) { caseIcon.Dispose(); caseIcon = null; }
            }
            language.Visible = true;
            letterCase.Visible = StatusVisible;
        }

        internal static Icon CreateIcon(InputState state, bool letterCase, Color? custom)
        {
            using (Bitmap image = CardPainter.RenderTaskbarIcon(state, letterCase, custom))
            using (var png = new MemoryStream())
            using (var ico = new MemoryStream())
            {
                image.Save(png, ImageFormat.Png);
                byte[] bytes = png.ToArray();
                using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, true))
                {
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                    writer.Write((byte)32); writer.Write((byte)32); writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(bytes.Length); writer.Write(22);
                    writer.Write(bytes);
                }
                ico.Position = 0;
                using (var icon = new Icon(ico)) return (Icon)icon.Clone();
            }
        }

        internal void RestoreAfterExplorerRestart()
        {
            language.Visible = false;
            letterCase.Visible = false;
            language.Visible = true;
            letterCase.Visible = StatusVisible;
        }

        internal void SaveWarning()
        {
            language.ShowBalloonTip(3000, "键盘状态", "设置暂时无法保存，本次使用仍然有效。", ToolTipIcon.Info);
        }

        public void Dispose()
        {
            language.Visible = false;
            letterCase.Visible = false;
            language.Dispose();
            letterCase.Dispose();
            if (languageIcon != null) languageIcon.Dispose();
            if (caseIcon != null) caseIcon.Dispose();
            appIcon.Dispose();
        }
    }
}
