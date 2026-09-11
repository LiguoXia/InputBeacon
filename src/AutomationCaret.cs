using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace InputBeacon
{
    // Native UIA exposes TextPattern2, which the .NET Framework client does not.
    // Interface order follows Microsoft's UIAutomationClient.h. Unused methods
    // reserve their COM slots; no focus, selection, or text mutation is invoked.
    internal sealed class AutomationCaret : IDisposable
    {
        private UiaClient client;
        private UiaWalker walker;
        internal string Status { get; private set; }

        private void Initialize()
        {
            if (client != null) return;
            client = (UiaClient)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e")));
            walker = client.RawViewWalker();
        }

        internal bool TryRead(IntPtr foreground, out Rectangle rectangle)
        {
            return TryReadControl(foreground, IntPtr.Zero, out rectangle);
        }

        internal bool TryReadControl(IntPtr foreground, IntPtr control, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            UiaElement element = null;
            object provider = null;
            UiaRange range = null;
            try
            {
                Initialize();
                element = control == IntPtr.Zero ? client.GetFocusedElement() : client.ElementFromHandle(control);
                if (element == null || (control == IntPtr.Zero && !Equals(element.Property(30008), true)) || Equals(element.Property(30022), true) || !BelongsToWindow(element, foreground))
                { Status = "UIA: no active text control"; return false; }
                try { provider = element.Pattern(10024); } catch (COMException) { }
                var pattern = provider as UiaText2;
                if (pattern == null) { Status = "UIA: TextPattern2 unavailable"; return false; }
                int active;
                range = pattern.GetCaretRange(out active);
                if (active == 0 || range == null) { Status = "UIA: caret inactive"; return false; }
                bool result = TryRange(range, out rectangle);
                Status = result ? "UIA TextPattern2" : "UIA: caret has no bounds";
                return result;
            }
            catch (COMException error) { Status = "UIA: " + error.ErrorCode.ToString("X8"); return false; }
            catch (InvalidCastException) { Status = "UIA: unsupported provider"; return false; }
            catch (NotImplementedException) { Status = "UIA: provider has no caret method"; return false; }
            catch (NotSupportedException) { Status = "UIA: unsupported caret method"; return false; }
            finally { Release(range); Release(provider); Release(element); }
        }

        private bool BelongsToWindow(UiaElement element, IntPtr foreground)
        {
            // Follow native HWND ancestry, not just process IDs: modern XAML
            // controls can be hosted by a different process inside Explorer.
            UiaElement current = element;
            try
            {
                for (int depth = 0; current != null && depth < 32; depth++)
                {
                    object handle = current.Property(30020);
                    IntPtr hwnd = handle is int ? new IntPtr((int)handle) : IntPtr.Zero;
                    if (hwnd == foreground || (hwnd != IntPtr.Zero && Native.IsChild(foreground, hwnd))) return true;
                    UiaElement parent = walker.GetParentElement(current);
                    if (!ReferenceEquals(current, element)) Release(current);
                    current = parent;
                }
                return false;
            }
            finally { if (!ReferenceEquals(current, element)) Release(current); }
        }

        internal static bool TryRange(UiaRange caret, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (FromBounds(caret.GetBoundingRectangles(), false, out rectangle)) return true;
            UiaRange character = null;
            try
            {
                character = caret.Clone();
                character.ExpandToEnclosingUnit(0); // TextUnit_Character
                bool atEnd = caret.CompareEndpoints(0, character, 1) == 0;
                if (FromBounds(character.GetBoundingRectangles(), atEnd, out rectangle)) return true;
            }
            finally { Release(character); character = null; }

            // Some XAML providers keep an expanded degenerate range empty.
            // Move only endpoints of a cloned range; never the user's selection.
            try
            {
                character = caret.Clone();
                if (character.MoveEndpointByUnit(1, 0, 1) > 0 &&
                    FromBounds(character.GetBoundingRectangles(), false, out rectangle)) return true;
            }
            finally { Release(character); character = null; }

            UiaRange line = null;
            try
            {
                line = caret.Clone();
                line.ExpandToEnclosingUnit(3); // TextUnit_Line
                // Do not put a blank line's hint at the end of the previous line.
                if (caret.CompareEndpoints(0, line, 0) <= 0) return false;
                character = caret.Clone();
                return character.MoveEndpointByUnit(0, 0, -1) < 0 &&
                    character.CompareEndpoints(0, line, 0) >= 0 &&
                    FromBounds(character.GetBoundingRectangles(), true, out rectangle);
            }
            finally { Release(character); Release(line); }
        }

        internal static bool FromBounds(double[] bounds, bool atEnd, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (bounds == null || bounds.Length != 4) return false;
            foreach (double value in bounds) if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 100000) return false;
            if (bounds[2] < 0 || bounds[3] <= 0 || bounds[3] >= 300) return false;
            rectangle = new Rectangle((int)Math.Round(bounds[0] + (atEnd ? bounds[2] : 0)), (int)Math.Round(bounds[1]), 1, (int)Math.Ceiling(bounds[3]));
            return CaretTracker.IsCaretRectangle(rectangle);
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        public void Dispose() { Release(walker); walker = null; Release(client); client = null; }
    }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface UiaClient
    {
        void CompareElements(); void CompareRuntimeIds(); void GetRootElement(); UiaElement ElementFromHandle(IntPtr window); void ElementFromPoint();
        UiaElement GetFocusedElement();
        void GetRootElementBuildCache(); void ElementFromHandleBuildCache(); void ElementFromPointBuildCache(); void GetFocusedElementBuildCache();
        void CreateTreeWalker(); void ControlViewWalker(); void ContentViewWalker();
        UiaWalker RawViewWalker();
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface UiaElement
    {
        void SetFocus(); void GetRuntimeId(); void FindFirst(); void FindAll(); void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
        [return: MarshalAs(UnmanagedType.Struct)] object Property(int id);
        void GetCurrentPropertyValueEx(); void GetCachedPropertyValue(); void GetCachedPropertyValueEx(); void GetCurrentPatternAs(); void GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)] object Pattern(int id);
    }

    [ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface UiaWalker { UiaElement GetParentElement(UiaElement element); }

    [ComImport, Guid("506a921a-fcc9-409f-b23b-37eb74106872"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface UiaText2
    {
        void RangeFromPoint(); void RangeFromChild(); void GetSelection(); void GetVisibleRanges(); void DocumentRange(); void SupportedTextSelection(); void RangeFromAnnotation();
        UiaRange GetCaretRange(out int active);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface UiaRange
    {
        UiaRange Clone();
        void Compare();
        int CompareEndpoints(int endpoint, UiaRange target, int targetEndpoint);
        void ExpandToEnclosingUnit(int unit);
        void FindAttribute(); void FindText(); void GetAttributeValue();
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] double[] GetBoundingRectangles();
        void GetEnclosingElement(); void GetText(); void Move();
        int MoveEndpointByUnit(int endpoint, int unit, int count);
    }
}
