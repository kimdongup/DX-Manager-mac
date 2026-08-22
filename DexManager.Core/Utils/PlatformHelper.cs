using System;

namespace DexManager.Utils
{
    public static class PlatformHelper
    {
        public static bool IsWindows => OperatingSystem.IsWindows();
        public static bool IsMacOS => OperatingSystem.IsMacOS();
        public static bool IsLinux => OperatingSystem.IsLinux();

        public static Version CurrentVersion => Environment.OSVersion.Version;

        public static bool RequiresLegacyAdb => IsWindows && CurrentVersion.Major < 10;

        public static string GetDisplayName()
        {
            if (IsMacOS)
            {
                return "macOS (" + CurrentVersion + ")";
            }

            if (IsLinux)
            {
                return "Linux (" + CurrentVersion + ")";
            }

            var version = CurrentVersion;
            if (version.Major == 6 && version.Minor == 1) return "Windows 7";
            if (version.Major == 6 && version.Minor == 2) return "Windows 8";
            if (version.Major == 6 && version.Minor == 3) return "Windows 8.1";
            if (version.Major >= 10 && version.Build >= 22000) return "Windows 11";
            if (version.Major >= 10) return "Windows 10";

            return "Windows " + version;
        }
    }
}
