using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public class BundledProcessCleanupServiceTests
    {
        [Fact]
        public void NormalizePathForComparison_StripsPrivatePrefixOnMac()
        {
            var normMethod = typeof(BundledProcessCleanupService).GetMethod(
                "NormalizePathForComparison",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(normMethod);

            if (OperatingSystem.IsMacOS())
            {
                var input = "/private/tmp/test.exe";
                var result = (string)normMethod.Invoke(null, new object[] { input });
                Assert.Equal("/tmp/test.exe", result);
            }

            var empty = (string)normMethod.Invoke(null, new object[] { "" });
            Assert.Equal(string.Empty, empty);
        }

        [Fact]
        public void AddExecutablePath_IgnoresNullOrEmpty()
        {
            var logService = new LogService();
            var service = new BundledProcessCleanupService(logService);

            service.AddExecutablePath(null);
            service.AddExecutablePath("");
            service.AddExecutablePath("   ");

            var terminated = service.TerminateRemainingProcesses();
            Assert.Equal(0, terminated);
        }

        [Fact]
        public void AddExecutablePath_DeduplicatesSamePath()
        {
            var logService = new LogService();
            var service = new BundledProcessCleanupService(logService);

            var path = "/tmp/test_binary";
            service.AddExecutablePath(path);
            service.AddExecutablePath(path);
            service.AddExecutablePath("/tmp/test_binary");

            var field = typeof(BundledProcessCleanupService).GetField(
                "_executablePaths",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);

            var list = (System.Collections.IList)field.GetValue(service);
            Assert.Single(list);
        }

        [Fact]
        public void TerminateRemainingProcesses_TerminatesOnlyConfiguredExecutablePath()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "DXManager.TestProcessCleanup." + Guid.NewGuid().ToString("N"));
            var ownedDir = Path.Combine(root, "owned");
            var unrelatedDir = Path.Combine(root, "unrelated");
            Directory.CreateDirectory(ownedDir);
            Directory.CreateDirectory(unrelatedDir);

            var scriptName = "dxmtest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var ownedScript = Path.Combine(ownedDir, scriptName);
            var unrelatedScript = Path.Combine(unrelatedDir, scriptName);

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.WriteAllText(ownedScript, "#!/bin/sh\nsleep 60\n");
                File.WriteAllText(unrelatedScript, "#!/bin/sh\nsleep 60\n");
                File.SetUnixFileMode(ownedScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(unrelatedScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Process ownedProcess = null;
            Process unrelatedProcess = null;

            try
            {
                ownedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = ownedScript,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                unrelatedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = unrelatedScript,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                Thread.Sleep(300);

                var cleanup = new BundledProcessCleanupService(new LogService());
                cleanup.AddExecutablePath(ownedScript);

                var count = cleanup.TerminateRemainingProcesses();
                Assert.Equal(1, count);

                Assert.True(ownedProcess.WaitForExit(3000), "Configured owned process must be terminated.");
                Assert.False(unrelatedProcess.HasExited, "Unrelated process in different path must remain running.");
            }
            finally
            {
                if (ownedProcess != null)
                {
                    try { if (!ownedProcess.HasExited) ownedProcess.Kill(); }
                    catch { }
                    ownedProcess.Dispose();
                }
                if (unrelatedProcess != null)
                {
                    try
                    {
                        if (!unrelatedProcess.HasExited)
                        {
                            unrelatedProcess.Kill();
                            unrelatedProcess.WaitForExit(2000);
                        }
                    }
                    catch { }
                    unrelatedProcess.Dispose();
                }
                try { Directory.Delete(root, true); }
                catch { }
            }
        }
    }
}
