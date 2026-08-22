using System;

namespace DexManager.Utils
{
    internal static class DeviceLogFormatter
    {
        public static string ForSerial(string serial, string message)
        {
            var normalized = string.IsNullOrWhiteSpace(serial)
                ? "unknown"
                : serial.Trim();
            return "[" + normalized + "] " + (message ?? string.Empty);
        }

        public static bool IsInformationalScrcpyErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            return line.IndexOf(
                    "file pushed, 0 skipped",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
