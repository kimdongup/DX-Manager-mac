using System;

namespace DexManager.Platform
{
    public interface IPlatformService
    {
        bool IsAdministrator();
        string GetOperatingSystemDisplayName();
        Version GetOperatingSystemVersion();
        bool RequiresLegacyAdb { get; }

        bool IsWindow(IntPtr handle);
        bool IsWindowVisible(IntPtr handle);
        bool IsIconic(IntPtr handle);
        void ShowWindow(IntPtr handle, bool restore);
        void SetForegroundWindow(IntPtr handle);
        IntPtr GetForegroundWindow();
        bool GetClientRect(IntPtr handle, out int left, out int top, out int right, out int bottom);
        bool GetWindowRect(IntPtr handle, out int left, out int top, out int right, out int bottom);
        void SuppressNativeCrashDialogs();

        bool IsDirectoryInProcessPath(string directory);
        bool IsDirectoryInSystemPath(string directory);
        bool TryRegisterDirectoryInSystemPath(string directory);
    }
}
