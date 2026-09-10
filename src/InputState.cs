using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace InputBeacon
{
    internal enum InputMode { Unknown, English, Chinese, Other }

    internal sealed class InputState
    {
        public InputMode Mode;
        public bool Caps, Shift, FullWidth;
        public string OtherLabel = "其他";
        public bool Uppercase { get { return Caps ^ Shift; } }
        public string ModeTitle
        {
            get
            {
                switch (Mode)
                {
                    case InputMode.English: return "英文";
                    case InputMode.Chinese: return "中文";
                    case InputMode.Other: return OtherLabel;
                    default: return "未知";
                }
            }
        }
        public string ModeSymbol
        {
            get
            {
                switch (Mode)
                {
                    case InputMode.English: return "英";
                    case InputMode.Chinese: return "中";
                    case InputMode.Other: return "语";
                    default: return "?";
                }
            }
        }
        public string ModeDetail
        {
            get
            {
                if (Mode == InputMode.Unknown) return "未读到输入法状态";
                if (FullWidth) return "全角输入";
                return Mode == InputMode.Other ? "当前键盘布局" : "当前输入模式";
            }
        }
        public string CaseTitle { get { return Uppercase ? "大写" : "小写"; } }
        public string CaseDetail { get { return "Caps " + (Caps ? "开" : "关") + (Shift ? " · Shift ↓" : ""); } }
        public string Key { get { return Mode + ":" + Caps + ":" + Shift + ":" + FullWidth + ":" + OtherLabel; } }
        public InputState Copy() { return (InputState)MemberwiseClone(); }

        internal static InputState Resolve(int language, bool isIme, bool gotOpen, uint open,
            bool gotConversion, uint conversion)
        {
            var state = new InputState { Mode = InputMode.Unknown };
            int primary = language & 0x3ff;
            if (primary == 9) { state.Mode = InputMode.English; return state; }
            bool eastAsian = primary == 4 || primary == 17 || primary == 18;
            if (language == 0) return state;
            if (!eastAsian && !isIme)
            {
                state.Mode = InputMode.Other;
                try { state.OtherLabel = CultureInfo.GetCultureInfo(language).TwoLetterISOLanguageName.ToUpperInvariant(); }
                catch (CultureNotFoundException) { state.OtherLabel = "其他"; }
                return state;
            }
            if (gotOpen && open == 0)
            {
                state.Mode = InputMode.English;
                return state;
            }
            if (!gotConversion) return state;
            state.FullWidth = (conversion & 8) != 0;
            if ((conversion & 1) == 0) state.Mode = InputMode.English;
            else if (primary == 4) state.Mode = InputMode.Chinese;
            else
            {
                state.Mode = InputMode.Other;
                state.OtherLabel = primary == 17 ? "日文" : primary == 18 ? "韩文" : "本地";
            }
            return state;
        }
    }

    internal sealed class InputProbe
    {
        internal InputState Read(out string diagnostic)
        {
            IntPtr foreground = Native.GetForegroundWindow();
            uint processId;
            uint threadId = Native.GetWindowThreadProcessId(foreground, out processId);
            if (foreground == IntPtr.Zero || threadId == 0)
            {
                diagnostic = "No foreground input window.";
                return WithKeys(new InputState());
            }
            var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf(typeof(Native.GuiThreadInfo)) };
            IntPtr target = foreground;
            if (Native.GetGUIThreadInfo(threadId, ref info) && info.Focus != IntPtr.Zero) target = info.Focus;
            uint focusThread = Native.GetWindowThreadProcessId(target, out processId);
            IntPtr layout = Native.GetKeyboardLayout(focusThread == 0 ? threadId : focusThread);
            int language = (int)(layout.ToInt64() & 0xffff);
            IntPtr ime = Native.ImmGetDefaultIMEWnd(target);
            uint open = 0, conversion = 0;
            bool gotOpen = Native.QueryIme(ime, Native.IMC_GETOPENSTATUS, out open);
            bool gotConversion = Native.QueryIme(ime, Native.IMC_GETCONVERSIONMODE, out conversion);
            // Some terminal hosts place their IME on the outer window's thread.
            if (!gotOpen && !gotConversion && target != foreground)
            {
                IntPtr fallback = Native.ImmGetDefaultIMEWnd(foreground);
                if (fallback != ime)
                {
                    ime = fallback;
                    gotOpen = Native.QueryIme(ime, Native.IMC_GETOPENSTATUS, out open);
                    gotConversion = Native.QueryIme(ime, Native.IMC_GETCONVERSIONMODE, out conversion);
                }
            }
            InputState state = InputState.Resolve(language, Native.ImmIsIME(layout), gotOpen, open, gotConversion, conversion);
            // Discard a sample taken across a focus switch instead of showing another app's mode.
            if (Native.GetForegroundWindow() != foreground) state = new InputState();
            diagnostic = string.Format(CultureInfo.InvariantCulture,
                "ForegroundClass={0}\r\nFocusClass={1}\r\nLayout=0x{2:X}\r\nImeWindow=0x{3:X}\r\nOpen={4}\r\nConversion={5}\r\nMode={6}",
                Native.ClassName(foreground), Native.ClassName(target), layout.ToInt64(), ime.ToInt64(),
                gotOpen ? open.ToString(CultureInfo.InvariantCulture) : "unavailable",
                gotConversion ? conversion.ToString(CultureInfo.InvariantCulture) : "unavailable", state.Mode);
            return WithKeys(state);
        }

        private static InputState WithKeys(InputState state)
        {
            state.Caps = (Native.GetKeyState(0x14) & 1) != 0;
            state.Shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
            return state;
        }
    }
}
