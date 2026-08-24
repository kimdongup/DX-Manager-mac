using DexManager.Platform;

namespace DexManager.Mac.Platform;

public sealed class MacPathProvider : IPathProvider
{
    public string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

    public string DefaultSettingsFilePath =>
        Path.Combine(BaseDirectory, "config", "settings.json");

    public string DefaultScreenshotFolder
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures) || !Directory.Exists(pictures))
            {
                var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                pictures = Path.Combine(userHome, "Pictures");
            }
            return Path.Combine(pictures, "DXManager");
        }
    }

    public string DefaultLogDirectory =>
        Path.Combine(BaseDirectory, "logs");

    public string DefaultProxyExecutablePath
    {
        get
        {
            var proxyDir = Path.Combine(BaseDirectory, "tools", "adb-proxy");
            var native = Path.Combine(proxyDir, "DXMAdbProxy");
            if (File.Exists(native)) return native;
            var dllInProxy = Path.Combine(proxyDir, "DXMAdbProxy.dll");
            if (File.Exists(dllInProxy)) return dllInProxy;
            var dllInBase = Path.Combine(BaseDirectory, "DXMAdbProxy.dll");
            if (File.Exists(dllInBase)) return dllInBase;
            return Path.Combine(proxyDir, "DXMAdbProxy.exe");
        }
    }

    public string ResolveDefaultAdbPath()
    {
        foreach (var path in GetCandidateAdbPaths())
        {
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        var pathAdb = FindInPath("adb");
        return !string.IsNullOrWhiteSpace(pathAdb) ? pathAdb : "/opt/homebrew/bin/adb";
    }

    public string ResolveDefaultScrcpyPath()
    {
        foreach (var path in GetCandidateScrcpyPaths())
        {
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        var pathScrcpy = FindInPath("scrcpy");
        return !string.IsNullOrWhiteSpace(pathScrcpy) ? pathScrcpy : "/opt/homebrew/bin/scrcpy";
    }

    public string ResolveWin7AdbPath() => ResolveDefaultAdbPath();

    public string[] GetCandidateAdbPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            // Prefer Homebrew native arm64 binaries on Apple Silicon
            "/opt/homebrew/bin/adb",
            "/usr/local/bin/adb",
            Path.Combine(BaseDirectory, "tools", "adb", "adb"),
            Path.Combine(BaseDirectory, "tools", "scrcpy", "adb"),
            Path.Combine(home, "Downloads", "scrcpy-macos-aarch64-v3.3.4", "adb"),
            Path.Combine(home, "Downloads", "scrcpy-macos-x86_64-v3.3.4", "adb"),
            Path.Combine(home, "Library", "Android", "sdk", "platform-tools", "adb"),
            Path.Combine(home, ".android-sdk", "platform-tools", "adb"),
            "/opt/android-sdk/platform-tools/adb",
            Path.Combine(BaseDirectory, "tools", "adb", "adb.exe")
        };

        var inPath = FindInPath("adb");
        if (!string.IsNullOrWhiteSpace(inPath) && !candidates.Contains(inPath))
        {
            candidates.Insert(0, inPath);
        }

        return [.. candidates];
    }

    public string[] GetCandidateScrcpyPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            // Prefer Homebrew native arm64 binaries on Apple Silicon
            "/opt/homebrew/bin/scrcpy",
            "/usr/local/bin/scrcpy",
            Path.Combine(BaseDirectory, "tools", "scrcpy", "scrcpy"),
            Path.Combine(home, "Downloads", "scrcpy-macos-aarch64-v3.3.4", "scrcpy"),
            Path.Combine(home, "Downloads", "scrcpy-macos-x86_64-v3.3.4", "scrcpy"),
            "/usr/bin/scrcpy",
            Path.Combine(BaseDirectory, "tools", "scrcpy", "scrcpy.exe")
        };

        var inPath = FindInPath("scrcpy");
        if (!string.IsNullOrWhiteSpace(inPath) && !candidates.Contains(inPath))
        {
            candidates.Insert(0, inPath);
        }

        return [.. candidates];
    }

    private static string FindInPath(string binaryName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;

        foreach (var segment in pathEnv.Split([':'], StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(segment.Trim(), binaryName);
                if (File.Exists(full))
                    return Path.GetFullPath(full);
            }
            catch
            {
                // Suppress path inspection errors
            }
        }

        return null;
    }
}
