using System;
using System.Diagnostics;
using System.IO;
using DexManager.Models;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public sealed class FileTransferCoordinatorProxyTests
    {
        [Theory]
        [InlineData(false, "DXMAdbProxy")]
        [InlineData(true, "DXMAdbProxy.exe")]
        public void GetDefaultProxyPath_UsesPlatformAppHost(
            bool isWindows,
            string expectedFileName)
        {
            var baseDirectory = Path.Combine(
                Path.GetTempPath(),
                "dxm-proxy-path");

            var path = FileTransferCoordinator.GetDefaultProxyPath(
                baseDirectory,
                isWindows);

            Assert.Equal(expectedFileName, Path.GetFileName(path));
            Assert.Equal(
                Path.Combine(Path.GetFullPath(baseDirectory), "tools", "adb-proxy"),
                Path.GetDirectoryName(path));
            Assert.NotEqual(".dll", Path.GetExtension(path));
        }

        [Fact]
        public void GetDotnetRoot_ReturnsRootAboveSharedFramework()
        {
            var root = Path.Combine(Path.GetTempPath(), "dxm-dotnet-root");
            var runtimeDirectory = Path.Combine(
                root,
                "shared",
                "Microsoft.NETCore.App",
                "8.0.0") + Path.DirectorySeparatorChar;

            var result = FileTransferCoordinator.GetDotnetRoot(
                runtimeDirectory);

            Assert.Equal(Path.GetFullPath(root), result);
        }

        [Fact]
        public void BeginSession_WhenProxyProbeFails_FallsBackToRealAdb()
        {
            var paths = CreateTemporaryExecutables();
            var probeCalls = 0;
            try
            {
                using (var coordinator = CreateCoordinator(
                    paths.RealAdb,
                    paths.Proxy,
                    (proxy, adb) =>
                    {
                        probeCalls++;
                        Assert.Equal(paths.Proxy, proxy);
                        Assert.Equal(paths.RealAdb, adb);
                        return false;
                    }))
                {
                    var sessionId = coordinator.BeginSession(
                        "device-1",
                        "Device 1",
                        "/sdcard/Download/");
                    var startInfo = new ProcessStartInfo();

                    coordinator.ConfigureScrcpyProcess(startInfo, sessionId);

                    Assert.Equal(string.Empty, sessionId);
                    Assert.Equal(paths.RealAdb,
                        startInfo.EnvironmentVariables["ADB"]);
                    Assert.Equal(1, probeCalls);
                }
            }
            finally
            {
                Directory.Delete(paths.Directory, true);
            }
        }

        [Fact]
        public void BeginSession_WhenProxyProbeSucceeds_ConfiguresProxyOnce()
        {
            var paths = CreateTemporaryExecutables();
            var probeCalls = 0;
            try
            {
                using (var coordinator = CreateCoordinator(
                    paths.RealAdb,
                    paths.Proxy,
                    (proxy, adb) =>
                    {
                        probeCalls++;
                        return true;
                    }))
                {
                    var firstSession = coordinator.BeginSession(
                        "device-1",
                        "Device 1",
                        "/sdcard/Download/");
                    var secondSession = coordinator.BeginSession(
                        "device-2",
                        "Device 2",
                        "/sdcard/Download/");
                    var startInfo = new ProcessStartInfo();

                    coordinator.ConfigureScrcpyProcess(
                        startInfo,
                        firstSession);

                    Assert.False(string.IsNullOrWhiteSpace(firstSession));
                    Assert.False(string.IsNullOrWhiteSpace(secondSession));
                    Assert.Equal(paths.Proxy,
                        startInfo.EnvironmentVariables["ADB"]);
                    Assert.Equal(paths.RealAdb,
                        startInfo.EnvironmentVariables["DXM_REAL_ADB"]);
                    Assert.Equal("1",
                        startInfo.EnvironmentVariables["DXM_TRANSFER_ENABLED"]);
                    Assert.Equal(1, probeCalls);

                    if (!OperatingSystem.IsWindows())
                    {
                        var dotnetRoot = startInfo.EnvironmentVariables[
                            "DOTNET_ROOT"];
                        Assert.False(string.IsNullOrWhiteSpace(dotnetRoot));
                        Assert.True(Directory.Exists(dotnetRoot));
                    }
                }
            }
            finally
            {
                Directory.Delete(paths.Directory, true);
            }
        }

        private static FileTransferCoordinator CreateCoordinator(
            string realAdbPath,
            string proxyPath,
            Func<string, string, bool> proxyProbe)
        {
            return new FileTransferCoordinator(
                realAdbPath,
                AppSettings.CreateDefault(),
                new LogService(),
                new DeviceRuntimeSessionRegistry(),
                proxyPath,
                proxyProbe);
        }

        private static TemporaryExecutables CreateTemporaryExecutables()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "dxm-proxy-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var realAdb = Path.Combine(directory, "adb");
            var proxy = Path.Combine(directory, "DXMAdbProxy");
            File.WriteAllText(realAdb, string.Empty);
            File.WriteAllText(proxy, string.Empty);
            return new TemporaryExecutables(directory, realAdb, proxy);
        }

        private sealed class TemporaryExecutables
        {
            internal TemporaryExecutables(
                string directory,
                string realAdb,
                string proxy)
            {
                Directory = directory;
                RealAdb = realAdb;
                Proxy = proxy;
            }

            internal string Directory { get; }
            internal string RealAdb { get; }
            internal string Proxy { get; }
        }
    }
}
