using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("键盘状态")]
[assembly: AssemblyDescription("当前窗口中英文输入模式与 Caps Lock / Shift 悬浮指示器")]
[assembly: AssemblyProduct("InputBeacon")]
[assembly: AssemblyVersion("1.4.3.0")]
[assembly: AssemblyFileVersion("1.4.3.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace InputBeacon
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--self-test")
                return SelfTests.Run(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output"));
            if (args.Length > 0 && args[0] == "--diagnose")
            {
                if (args.Length < 2) return 2;
                string diagnostic;
                InputState state = new InputProbe().Read(out diagnostic);
                File.WriteAllText(args[1], "InputBeacon 1.4.3\r\n" + diagnostic + "\r\nCaps=" + state.Caps + "\r\nShift=" + state.Shift, Encoding.UTF8);
                return 0;
            }
            if (args.Length > 0) return 2;

            string identity = WindowsIdentity.GetCurrent().User.Value;
            string name = @"Local\InputBeacon.1." + identity;
            using (var mutex = new Mutex(false, name))
            using (var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show"))
            {
                bool ownsMutex;
                try { ownsMutex = mutex.WaitOne(0, false); }
                catch (AbandonedMutexException) { ownsMutex = true; }
                if (!ownsMutex) { showRequest.Set(); return 0; }
                try
                {
                    Application.Run(new Overlay(Settings.Load(Settings.FilePath), showRequest, false));
                    return 0;
                }
                catch (Exception error)
                {
                    MessageBox.Show("键盘状态未能启动：\n" + error.Message, "键盘状态", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
