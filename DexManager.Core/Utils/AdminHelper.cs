using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DexManager.Utils
{
    public static class AdminHelper
    {
        [DllImport("libc", EntryPoint = "geteuid", SetLastError = true)]
        private static extern uint geteuid();

        public static bool IsAdministrator()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using (var identity = WindowsIdentity.GetCurrent())
                    {
                        var principal = new WindowsPrincipal(identity);
                        return principal.IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }

                if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                {
                    return geteuid() == 0;
                }
            }
            catch
            {
                // Fallback on any inspection failure
            }

            return false;
        }
    }
}
