using System;
using System.Runtime.InteropServices;

namespace DexManager.Utils
{
    internal static class NativeWindowHelper
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        public static bool CheckIsWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    return IsWindow(hWnd);
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }
    }
}
