using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace InputBeacon
{
    internal static class MemoryDiagnostics
    {
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

        // Collected only when the user asks for diagnostics, never on each tick.
        internal static string Read()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "WorkingSetMiB={0:F1}\r\nPrivateMiB={1:F1}\r\nManagedMiB={2:F1}\r\nHandles={3}\r\nGDI={4}\r\nUSER={5}\r\nUptimeHours={6:F1}",
                    process.WorkingSet64 / 1048576.0, process.PrivateMemorySize64 / 1048576.0,
                    GC.GetTotalMemory(false) / 1048576.0, process.HandleCount,
                    GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1),
                    (DateTime.Now - process.StartTime).TotalHours);
            }
        }
    }
}
