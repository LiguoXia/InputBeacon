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
        internal string InputIdentity { get; private set; }
        internal long GeometryTimestamp { get; private set; }

        internal string ReadFocusIdentity(IntPtr foreground)
        {
            return ReadControlIdentity(foreground, IntPtr.Zero);
        }

        internal string ReadControlIdentity(IntPtr foreground, IntPtr control)
        {
            UiaElement element = null;
            try
            {
                Initialize();
                element = control == IntPtr.Zero ? client.GetFocusedElement() : client.ElementFromHandle(control);
                if (element == null || (control == IntPtr.Zero && !Equals(element.Property(30008), true)) ||
                    Equals(element.Property(30022), true) || !BelongsToWindow(element, foreground)) return null;
                return Identity(element);
            }
            catch (COMException) { return null; }
            catch (InvalidCastException) { return null; }
            catch (NotSupportedException) { return null; }
            finally { Release(element); }
        }

        private void Initialize()
        {
            if (client != null) return;
            UiaClient next = (UiaClient)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e")));
            try { walker = next.RawViewWalker(); client = next; }
            catch { Release(next); throw; }
        }

        internal bool TryRead(IntPtr foreground, out Rectangle rectangle)
        {
            return TryReadControl(foreground, IntPtr.Zero, out rectangle);
        }

        internal bool TryReadControl(IntPtr foreground, IntPtr control, out Rectangle rectangle)
        {
            return TryReadControl(foreground, control, false, out rectangle);
        }

        internal bool TryReadSelectionControl(IntPtr foreground, IntPtr control, out Rectangle rectangle)
        {
            return TryReadControl(foreground, control, true, out rectangle);
        }

        private bool TryReadControl(IntPtr foreground, IntPtr control, bool selectionOnly, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            InputIdentity = null;
            UiaElement element = null;
            try
            {
                Initialize();
                element = control == IntPtr.Zero ? client.GetFocusedElement() : client.ElementFromHandle(control);
                if (element == null || (control == IntPtr.Zero && !Equals(element.Property(30008), true)) || Equals(element.Property(30022), true) || !BelongsToWindow(element, foreground))
                { Status = "UIA: no active text control"; return false; }
                bool result = !selectionOnly && TryCaretPattern(element, out rectangle);
                Status = "UIA TextPattern2";
                if (!result)
                {
                    result = TrySelection(element, out rectangle);
                    if (result) Status = "UIA TextPattern";
                }
                if (result)
                {
                    GeometryTimestamp = CaretTracker.Now;
                    InputIdentity = Identity(element);
                }
                return result;
            }
            catch (COMException error) { Status = "UIA: " + error.ErrorCode.ToString("X8"); return false; }
            catch (InvalidCastException) { Status = "UIA: unsupported provider"; return false; }
            catch (NotImplementedException) { Status = "UIA: provider has no caret method"; return false; }
            catch (NotSupportedException) { Status = "UIA: unsupported caret method"; return false; }
            finally { Release(element); }
        }

        private static string Identity(UiaElement element)
        {
            try
            {
                int[] id = element.GetRuntimeId();
                return id == null || id.Length == 0 ? null : string.Join(",", id);
            }
            catch (COMException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static bool TryCaretPattern(UiaElement element, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            object provider = null;
            UiaRange range = null;
            try
            {
                provider = element.Pattern(10024);
                var pattern = provider as UiaText2;
                if (pattern == null) return false;
                int active;
                range = pattern.GetCaretRange(out active);
                return active != 0 && range != null && TryRange(range, out rectangle);
            }
            catch (COMException) { return false; }
            catch (NotImplementedException) { return false; }
            catch (NotSupportedException) { return false; }
            finally { Release(range); Release(provider); }
        }

        // Use native COM for the legacy fallback too. Each acquired range, array
        // and provider is released in this poll instead of waiting for finalizers.
        private bool TrySelection(UiaElement element, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            object provider = null;
            UiaRangeArray ranges = null;
            UiaRange range = null;
            try
            {
                provider = element.Pattern(10014);
                var pattern = provider as UiaText;
                if (pattern == null) { Status = "UIA: TextPattern unavailable"; return false; }
                ranges = pattern.GetSelection();
                if (ranges == null || ranges.Length() != 1) { Status = "UIA: no single selection"; return false; }
                range = ranges.GetElement(0);
                if (range == null || range.CompareEndpoints(0, range, 1) != 0) { Status = "UIA: selection is not a caret"; return false; }
                Status = "UIA: caret has no bounds";
                return TryRange(range, out rectangle);
            }
            finally { Release(range); Release(ranges); Release(provider); }
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
        void SetFocus();
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] GetRuntimeId();
        void FindFirst(); void FindAll(); void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
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

    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface UiaText
    {
        void RangeFromPoint(); void RangeFromChild(); UiaRangeArray GetSelection();
    }

    [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface UiaRangeArray
    {
        int Length(); UiaRange GetElement(int index);
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
