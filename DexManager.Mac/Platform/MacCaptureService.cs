using System.Diagnostics;
using DexManager.Models;
using DexManager.Platform;

namespace DexManager.Mac.Platform;

public sealed class MacCaptureService : ICaptureService
{
    private readonly string _screenshotFolder;

    public MacCaptureService(string screenshotFolder = null)
    {
        _screenshotFolder = screenshotFolder;
        if (string.IsNullOrWhiteSpace(_screenshotFolder))
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures) || !Directory.Exists(pictures))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                pictures = Path.Combine(home, "Pictures");
            }
            _screenshotFolder = Path.Combine(pictures, "DXManager");
        }
    }

    public CaptureResult CaptureWindow(IntPtr windowHandle, string serial)
    {
        Directory.CreateDirectory(_screenshotFolder);
        var fileName = $"DeX_Full_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var targetPath = Path.Combine(_screenshotFolder, fileName);

        // On macOS, screencapture CLI allows capturing window or full screen
        var arguments = windowHandle != IntPtr.Zero
            ? $"-x -l {windowHandle.ToInt64()} \"{targetPath}\""
            : $"-x \"{targetPath}\"";

        RunScreencapture(arguments);
        return new CaptureResult(targetPath, string.Empty, false);
    }

    public CaptureResult CaptureScreenRectangle(int x, int y, int width, int height, string prefix, string serial)
    {
        Directory.CreateDirectory(_screenshotFolder);
        var tag = string.IsNullOrWhiteSpace(prefix) ? "Capture" : prefix;
        var fileName = $"{tag}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var targetPath = Path.Combine(_screenshotFolder, fileName);

        var arguments = $"-x -R{x},{y},{width},{height} \"{targetPath}\"";
        RunScreencapture(arguments);
        return new CaptureResult(targetPath, string.Empty, false);
    }

    public Task<CaptureResult> CaptureWindowAsync(IntPtr windowHandle, string serial) =>
        Task.Run(() => CaptureWindow(windowHandle, serial));

    public Task<CaptureResult> CaptureScreenRectangleAsync(int x, int y, int width, int height, string prefix, string serial) =>
        Task.Run(() => CaptureScreenRectangle(x, y, width, height, prefix, serial));

    private static void RunScreencapture(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/sbin/screencapture",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(3000);
        }
        catch
        {
            // Suppress screencapture background errors
        }
    }
}
