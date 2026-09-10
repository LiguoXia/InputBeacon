using System;
using System.Runtime.InteropServices;
using System.Text;

namespace InputBeacon
{
    internal static class Native
    {
        internal const int WM_IME_CONTROL = 0x0283;
        internal const int IMC_GETCONVERSIONMODE = 1;
        internal const int IMC_GETOPENSTATUS = 5;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int GWL_EXSTYLE = -20;
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct GuiThreadInfo
        {
            public int Size;
            public uint Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rect CaretRect;
        }

        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
        [DllImport("user32.dll")] internal static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("imm32.dll")] internal static extern IntPtr ImmGetDefaultIMEWnd(IntPtr window);
        [DllImport("imm32.dll")] internal static extern bool ImmIsIME(IntPtr layout);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
            IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after,
            int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLong64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong32(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLong64(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        internal static long GetStyle(IntPtr window)
        {
            return IntPtr.Size == 8 ? GetWindowLong64(window, GWL_EXSTYLE).ToInt64() : GetWindowLong32(window, GWL_EXSTYLE);
        }

        internal static void SetStyle(IntPtr window, long value)
        {
            if (IntPtr.Size == 8) SetWindowLong64(window, GWL_EXSTYLE, new IntPtr(value));
            else SetWindowLong32(window, GWL_EXSTYLE, (int)value);
        }

        internal static string ClassName(IntPtr window)
        {
            var text = new StringBuilder(128);
            GetClassName(window, text, text.Capacity);
            return text.ToString();
        }

        internal static bool QueryIme(IntPtr window, int command, out uint value)
        {
            value = 0;
            if (window == IntPtr.Zero) return false;
            UIntPtr result;
            // Keep a hung or elevated target from freezing the indicator. A zero result
            // is a valid mode; a zero API return is a failure and must stay UNKNOWN.
            IntPtr success = SendMessageTimeout(window, WM_IME_CONTROL, new IntPtr(command),
                IntPtr.Zero, 0x0001 | 0x0002 | 0x0020, 35, out result);
            if (success == IntPtr.Zero || result.ToUInt64() > uint.MaxValue) return false;
            value = (uint)result.ToUInt64();
            return value != uint.MaxValue;
        }
    }
}
