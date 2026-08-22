using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DexManager.Services;
using DexManager.Utils;
using Xunit;

namespace DexManager.Tests
{
    public class ProcessRunnerTests
    {
        [Fact]
        public void BlocksNewProcessesAfterShutdown()
        {
            var runner = new ProcessRunner(new LogService());
            runner.BeginShutdown();

            var result = runner.Run(
                GetCommandProcessorPath(),
                GetCommandProcessorArgs(),
                null,
                5000,
                false);

            Assert.True(result.Canceled, "shutdown gate must cancel new child process launches");
            Assert.False(result.IsSuccess, "a shutdown-canceled process must not report success");
        }

        [Fact]
        public async Task TerminatesActiveProcessOnShutdown()
        {
            var runner = new ProcessRunner(new LogService());
            var task = Task.Run(delegate
            {
                return runner.Run(
                    GetPingExecutablePath(),
                    GetPingArguments(),
                    null,
                    30000,
                    false);
            });

            await Task.Delay(300);
            runner.BeginShutdown();

            var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.Canceled, "terminated child process must report shutdown cancellation");
        }

        private static string GetCommandProcessorPath()
        {
            if (OperatingSystem.IsWindows()) return "cmd.exe";
            return "/bin/sh";
        }

        private static string GetCommandProcessorArgs()
        {
            if (OperatingSystem.IsWindows()) return "/d /c exit 0";
            return "-c \"exit 0\"";
        }

        private static string GetPingExecutablePath()
        {
            if (OperatingSystem.IsWindows()) return "ping.exe";
            if (File.Exists("/sbin/ping")) return "/sbin/ping";
            if (File.Exists("/bin/ping")) return "/bin/ping";
            if (File.Exists("/usr/bin/ping")) return "/usr/bin/ping";
            return "/bin/sleep";
        }

        private static string GetPingArguments()
        {
            if (OperatingSystem.IsWindows()) return "127.0.0.1 -n 30";
            var exe = GetPingExecutablePath();
            if (exe.Contains("sleep")) return "30";
            return "-c 30 127.0.0.1";
        }
    }
}
