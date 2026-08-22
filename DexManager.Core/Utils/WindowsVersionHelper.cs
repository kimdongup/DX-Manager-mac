using System;

namespace DexManager.Utils
{
    public static class WindowsVersionHelper
    {
        public static Version CurrentVersion
        {
            get { return PlatformHelper.CurrentVersion; }
        }

        public static bool RequiresLegacyAdb
        {
            get { return PlatformHelper.RequiresLegacyAdb; }
        }

        public static string GetDisplayName()
        {
            return PlatformHelper.GetDisplayName();
        }
    }
}
