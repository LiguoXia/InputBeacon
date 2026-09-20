using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace InputBeacon
{
    // Uses the bridge supplied by the foreground Java application's own runtime.
    // No DLL injection, bundled JVM, text reads, or automatic IDE configuration.
    internal sealed class JavaCaret : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] internal struct TextInfo { internal int Count, Caret, AtPoint; }
        [StructLayout(LayoutKind.Sequential)] internal struct TextRectangle { internal int X, Y, Width, Height; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RunBridge();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool IsJava(IntPtr window);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool Focus(IntPtr window, out int vm, out long context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool GetInfo(int vm, long context, out TextInfo info, int x, int y);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool GetRectangle(int vm, long context, out TextRectangle rectangle, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseObject(int vm, long context);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        private IntPtr module;
        private IsJava isJava;
        private Focus getFocus;
        private GetInfo getInfo;
        private GetRectangle getCaret, getCharacter;
        private ReleaseObject release;
        private uint lastProcess;
        private string runtime;
        internal string Status { get; private set; }
        internal bool IsInitialized { get { return module != IntPtr.Zero; } }
        internal static string LastRuntime { get; private set; }

        internal static string FindRuntime(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    string executable = process.MainModule.FileName;
                    string bin = Path.GetDirectoryName(executable);
                    string root = Path.GetDirectoryName(bin);
                    foreach (string candidate in new[] { bin, Path.Combine(root, "jbr", "bin"), Path.Combine(root, "jre", "bin") })
                        if (File.Exists(Path.Combine(candidate, LibraryName))) return candidate;
                }
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        internal static string LibraryName { get { return IntPtr.Size == 8 ? "windowsaccessbridge-64.dll" : "windowsaccessbridge-32.dll"; } }

        private T Export<T>(string name) where T : class
        {
            IntPtr address = GetProcAddress(module, name);
            if (address == IntPtr.Zero) throw new EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        private bool Initialize(IntPtr foreground)
        {
            uint process;
            Native.GetWindowThreadProcessId(foreground, out process);
            if (process != lastProcess)
            {
                lastProcess = process;
                runtime = FindRuntime(process);
                if (runtime != null) LastRuntime = runtime;
            }
            if (runtime == null) { Status = "Java: runtime not available"; return false; }
            if (module != IntPtr.Zero) return true;
            module = LoadLibraryEx(Path.Combine(runtime, LibraryName), IntPtr.Zero, 0x00000100 | 0x00000800);
            if (module == IntPtr.Zero) { Status = "Java: bridge load failed " + Marshal.GetLastWin32Error(); return false; }
            try
            {
                isJava = Export<IsJava>("isJavaWindow");
                getFocus = Export<Focus>("getAccessibleContextWithFocus");
                getInfo = Export<GetInfo>("getAccessibleTextInfo");
                getCaret = Export<GetRectangle>("getCaretLocation");
                getCharacter = Export<GetRectangle>("getAccessibleTextRect");
                release = Export<ReleaseObject>("releaseJavaObject");
                Export<RunBridge>("Windows_run")();
                return true;
            }
            catch (EntryPointNotFoundException) { FreeLibrary(module); module = IntPtr.Zero; Status = "Java: bridge exports unavailable"; return false; }
        }

        internal bool TryRead(IntPtr foreground, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (!Native.ClassName(foreground).StartsWith("SunAwt", StringComparison.Ordinal)) return false;
            if (!Initialize(foreground)) return false;
            if (!isJava(foreground)) { Status = "Java: enable Java Access Bridge and restart the IDE"; return false; }
            int vm;
            long context;
            if (!getFocus(foreground, out vm, out context) || context == 0) { Status = "Java: no focused context"; return false; }
            try
            {
                TextInfo info;
                if (!getInfo(vm, context, out info, 0, 0) || info.Caret < 0) { Status = "Java: focused component is not text"; return false; }
                TextRectangle value;
                if (getCaret(vm, context, out value, info.Caret) && Valid(value))
                    rectangle = ToCaret(value, false);
                else if (info.Caret < info.Count && getCharacter(vm, context, out value, info.Caret) && Valid(value))
                    rectangle = ToCaret(value, false);
                else if (info.Caret > 0 && getCharacter(vm, context, out value, info.Caret - 1) && Valid(value))
                    rectangle = ToCaret(value, true);
                else { Status = "Java: caret geometry unavailable"; return false; }
                Status = "Java Access Bridge";
                return CaretTracker.IsCaretRectangle(rectangle);
            }
            finally { release(vm, context); }
        }

        internal static bool Valid(TextRectangle value)
        {
            return value.Width >= 0 && value.Width < 200 && CaretTracker.IsCaretRectangle(new Rectangle(value.X, value.Y, value.Width, value.Height));
        }
        internal static Rectangle ToCaret(TextRectangle value, bool end)
        {
            return new Rectangle(value.X + (end ? value.Width : 0), value.Y, 1, value.Height);
        }
        public void Dispose()
        {
            if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
        }
    }
}
