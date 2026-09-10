using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace InputBeacon
{
    internal sealed class Settings
    {
        internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputBeacon");
        internal static readonly string FilePath = Path.Combine(Folder, "settings.ini");
        internal int X = int.MinValue, Y = int.MinValue;
        internal int Scale = 100, Opacity = 94;
        internal bool ClickThrough;
        internal bool UseCustomTextColor;
        internal Color TextColor = ColorValue.Default;
        internal bool ShowFloating = true;
        internal bool ShowTaskbarStatus;
        internal bool FollowCaret;
        internal int FollowSeconds = 3;

        internal static Settings Load(string path)
        {
            var settings = new Settings();
            try
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string[] pair = line.Split('=');
                    if (pair.Length != 2) continue;
                    if (pair[0] == "TextColor")
                    {
                        Color parsed;
                        if (ColorValue.TryParse(pair[1], out parsed)) settings.TextColor = parsed;
                        continue;
                    }
                    int number;
                    if (!int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) continue;
                    switch (pair[0])
                    {
                        case "X": settings.X = number; break;
                        case "Y": settings.Y = number; break;
                        case "Scale": if (number == 80 || number == 100 || number == 125 || number == 150) settings.Scale = number; break;
                        case "Opacity": if (number >= 45 && number <= 100) settings.Opacity = number; break;
                        case "ClickThrough": settings.ClickThrough = number == 1; break;
                        case "UseCustomTextColor": settings.UseCustomTextColor = number == 1; break;
                        case "ShowFloating": settings.ShowFloating = number != 0; break;
                        case "ShowTaskbarStatus": settings.ShowTaskbarStatus = number == 1; break;
                        case "FollowCaret": settings.FollowCaret = number == 1; break;
                        case "FollowSeconds": if (number >= 0 && number <= 60) settings.FollowSeconds = number; break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return settings;
        }

        internal bool Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                string content = string.Format(CultureInfo.InvariantCulture, "X={0}\r\nY={1}\r\nScale={2}\r\nOpacity={3}\r\nClickThrough={4}\r\nUseCustomTextColor={5}\r\nTextColor={6}\r\nShowFloating={7}\r\nShowTaskbarStatus={8}\r\n", X, Y, Scale, Opacity, ClickThrough ? 1 : 0, UseCustomTextColor ? 1 : 0, ColorValue.Hex(TextColor), ShowFloating ? 1 : 0, ShowTaskbarStatus ? 1 : 0);
                string temporary = path + ".tmp";
                content += string.Format(CultureInfo.InvariantCulture, "FollowCaret={0}\r\nFollowSeconds={1}\r\n", FollowCaret ? 1 : 0, FollowSeconds);
                File.WriteAllText(temporary, content, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static Point Clamp(Point point, Size size, Rectangle area)
        {
            return new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - size.Width)),
                Math.Max(area.Top, Math.Min(point.Y, area.Bottom - size.Height)));
        }

        internal static bool StartsWithWindows
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                        return key != null && key.GetValue("InputBeacon") != null;
                }
                catch (System.Security.SecurityException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }

        internal static void SetStartsWithWindows(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("InputBeacon", "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                else key.DeleteValue("InputBeacon", false);
            }
        }
    }
}
