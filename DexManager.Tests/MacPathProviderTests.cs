using System;
using System.IO;
using System.Linq;
using DexManager.Mac.Platform;
using DexManager.Models;
using DexManager.Platform;
using DexManager.Services;
using DexManager.Utils;
using Xunit;

namespace DexManager.Tests
{
    public class MacPathProviderTests
    {
        private readonly MacPathProvider _provider;

        public MacPathProviderTests()
        {
            _provider = new MacPathProvider();
        }

        [Fact]
        public void BaseDirectory_IsNotNullOrEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(_provider.BaseDirectory));
            Assert.True(Directory.Exists(_provider.BaseDirectory));
        }

        [Fact]
        public void DefaultSettingsFilePath_PointsToConfigSettingsJson()
        {
            var path = _provider.DefaultSettingsFilePath;
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.EndsWith(Path.Combine("config", "settings.json"), path);
            Assert.StartsWith(_provider.BaseDirectory, path);
        }

        [Fact]
        public void DefaultLogDirectory_PointsToLogsSubfolder()
        {
            var path = _provider.DefaultLogDirectory;
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.EndsWith("logs", path);
            Assert.StartsWith(_provider.BaseDirectory, path);
        }

        [Fact]
        public void DefaultScreenshotFolder_ContainsPicturesDXManager()
        {
            var path = _provider.DefaultScreenshotFolder;
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Contains(Path.Combine("Pictures", "DXManager"), path);
        }

        [Fact]
        public void DefaultProxyExecutablePath_PointsToAdbProxy()
        {
            var path = _provider.DefaultProxyExecutablePath;
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Contains(Path.Combine("tools", "adb-proxy"), path);
            Assert.Equal("DXMAdbProxy", Path.GetFileName(path));
            Assert.NotEqual(".dll", Path.GetExtension(path));
        }

        [Fact]
        public void GetCandidateAdbPaths_ContainsHomebrewAndAndroidSdkLocations()
        {
            var candidates = _provider.GetCandidateAdbPaths();
            Assert.NotNull(candidates);
            Assert.NotEmpty(candidates);

            Assert.Contains(candidates, p => p.Contains("/opt/homebrew/bin/adb"));
            Assert.Contains(candidates, p => p.Contains("/usr/local/bin/adb"));
            Assert.Contains(candidates, p => p.Contains(Path.Combine("Library", "Android", "sdk", "platform-tools", "adb")));
            Assert.Contains(candidates, p => p.Contains(Path.Combine(".android-sdk", "platform-tools", "adb")));
            Assert.Contains(candidates, p => p.Contains("/opt/android-sdk/platform-tools/adb"));
        }

        [Fact]
        public void GetCandidateScrcpyPaths_ContainsHomebrewAndLocalLocations()
        {
            var candidates = _provider.GetCandidateScrcpyPaths();
            Assert.NotNull(candidates);
            Assert.NotEmpty(candidates);

            Assert.Contains(candidates, p => p.Contains("/opt/homebrew/bin/scrcpy"));
            Assert.Contains(candidates, p => p.Contains("/usr/local/bin/scrcpy"));
            Assert.Contains(candidates, p => p.Contains("/usr/bin/scrcpy"));
        }

        [Fact]
        public void ResolveDefaultAdbPath_ReturnsValidString()
        {
            var resolved = _provider.ResolveDefaultAdbPath();
            Assert.False(string.IsNullOrWhiteSpace(resolved));
            // Either an existing binary or fallback /opt/homebrew/bin/adb
            Assert.True(File.Exists(resolved) || resolved == "/opt/homebrew/bin/adb");
        }

        [Fact]
        public void ResolveDefaultScrcpyPath_ReturnsValidString()
        {
            var resolved = _provider.ResolveDefaultScrcpyPath();
            Assert.False(string.IsNullOrWhiteSpace(resolved));
            Assert.True(File.Exists(resolved) || resolved == "/opt/homebrew/bin/scrcpy");
        }

        [Fact]
        public void ResolveWin7AdbPath_ReturnsSameAsDefaultAdbPath()
        {
            var win7Adb = _provider.ResolveWin7AdbPath();
            var defaultAdb = _provider.ResolveDefaultAdbPath();
            Assert.Equal(defaultAdb, win7Adb);
        }

        [Fact]
        public void PathService_IsAdbDirectoryInProcessPath_HandlesUnixColonSeparator()
        {
            var logService = new LogService();
            var settingsService = new SettingsService(logService);
            var processRunner = new ProcessRunner(logService);
            var pathService = new PathService(settingsService, logService, processRunner, _provider);

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var segments = pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length > 0)
            {
                var firstDir = segments[0].Trim();
                var mockAdbPath = Path.Combine(firstDir, "adb");
                Assert.True(pathService.IsAdbDirectoryInProcessPath(mockAdbPath));
            }

            Assert.False(pathService.IsAdbDirectoryInProcessPath(null));
            Assert.False(pathService.IsAdbDirectoryInProcessPath(string.Empty));
            Assert.False(pathService.IsAdbDirectoryInProcessPath("/nonexistent_folder_xyz_12345/adb"));
        }

        [Fact]
        public void PathService_SelectAdbPath_ThrowsOnNullSettings()
        {
            var logService = new LogService();
            var settingsService = new SettingsService(logService);
            var processRunner = new ProcessRunner(logService);
            var pathService = new PathService(settingsService, logService, processRunner, _provider);

            Assert.Throws<ArgumentNullException>(() => pathService.SelectAdbPath(null, 3000));
        }

        [Fact]
        public void PathService_SelectAdbPath_ThrowsWhenManualAdbNotFound()
        {
            var logService = new LogService();
            var settingsService = new SettingsService(logService);
            var processRunner = new ProcessRunner(logService);
            var pathService = new PathService(settingsService, logService, processRunner, _provider);

            var settings = AppSettings.CreateDefault();
            settings.Paths.AdbSelectionMode = AdbSelectionMode.Manual;
            settings.Paths.AdbPath = "/nonexistent/path/to/adb";

            Assert.Throws<FileNotFoundException>(() => pathService.SelectAdbPath(settings, 3000));
        }
    }
}
