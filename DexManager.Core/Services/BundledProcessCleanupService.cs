using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DexManager.Services
{
    /// <summary>
    /// Final safety net for helper executables shipped with DX Manager.
    /// Normal service shutdown still owns the primary process lifecycle; this
    /// class removes detached ADB servers and any orphaned bundled helpers after
    /// the WinForms message loop has ended.
    /// </summary>
    public sealed class BundledProcessCleanupService
    {
        private const int ExitWaitMs = 1500;
        private const int SweepCount = 2;
        private readonly object _sync = new object();
        private readonly List<string> _executablePaths =
            new List<string>();
        private readonly LogService _logService;

        [DllImport("libproc", EntryPoint = "proc_pidpath", CallingConvention = CallingConvention.Cdecl)]
        private static extern int proc_pidpath(int pid, IntPtr buffer, uint buffersize);

        [DllImport("libSystem", EntryPoint = "proc_pidpath", CallingConvention = CallingConvention.Cdecl)]
        private static extern int proc_pidpath_fallback(int pid, IntPtr buffer, uint buffersize);

        public BundledProcessCleanupService(LogService logService)
        {
            _logService = logService ??
                throw new ArgumentNullException("logService");
        }

        public void AddExecutablePath(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return;

            string fullPath;
            try
            {
                fullPath = NormalizePathForComparison(executablePath);
            }
            catch
            {
                return;
            }

            lock (_sync)
            {
                foreach (var existing in _executablePaths)
                {
                    if (string.Equals(
                        existing,
                        fullPath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                _executablePaths.Add(fullPath);
            }
        }

        public int TerminateRemainingProcesses()
        {
            string[] paths;
            lock (_sync) paths = _executablePaths.ToArray();

            var terminatedProcessIds = new HashSet<int>();
            for (var sweep = 0; sweep < SweepCount; sweep++)
            {
                foreach (var path in paths)
                    TerminateProcessesAtPath(path, terminatedProcessIds);
            }

            if (terminatedProcessIds.Count > 0)
            {
                _logService.Info(LocalizationService.Format(
                    "Log.Process.FinalSweepTerminated",
                    terminatedProcessIds.Count));
            }
            return terminatedProcessIds.Count;
        }

        private static void TerminateProcessesAtPath(
            string expectedPath,
            ISet<int> terminatedProcessIds)
        {
            var processName = Path.GetFileNameWithoutExtension(expectedPath);
            if (string.IsNullOrWhiteSpace(processName)) return;

            var normalizedExpected = NormalizePathForComparison(expectedPath);

            Process[] processes;
            try
            {
                var shortName = processName.Length > 15 ? processName.Substring(0, 15) : processName;
                processes = Process.GetProcessesByName(shortName);
                if (processes.Length == 0 && (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()))
                {
                    // Fallback to all processes only if GetProcessesByName found nothing
                    processes = Process.GetProcesses();
                }
            }
            catch
            {
                return;
            }

            int currentProcessId;
            using (var currentProcess = Process.GetCurrentProcess())
                currentProcessId = currentProcess.Id;

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == currentProcessId ||
                            process.HasExited)
                        {
                            continue;
                        }

                        var isMatch = false;
                        var actualPath = GetProcessExecutablePath(process);
                        if (!string.IsNullOrWhiteSpace(actualPath))
                        {
                            var normalizedActual = NormalizePathForComparison(actualPath);
                            if (string.Equals(
                                normalizedActual,
                                normalizedExpected,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                isMatch = true;
                            }
                        }

                        // If not matched by executable path, check command line only for shell wrappers on macOS
                        if (!isMatch && OperatingSystem.IsMacOS() &&
                            (string.IsNullOrEmpty(actualPath) ||
                             actualPath.EndsWith("/sh") ||
                             actualPath.EndsWith("/bash") ||
                             actualPath.EndsWith("/zsh") ||
                             actualPath.Contains(processName)))
                        {
                            try
                            {
                                var psi = new ProcessStartInfo("/bin/ps", $"-p {process.Id} -o command=")
                                {
                                    RedirectStandardOutput = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                using (var ps = Process.Start(psi))
                                {
                                    var cmd = ps.StandardOutput.ReadToEnd();
                                    if (cmd != null && (cmd.Contains(expectedPath) || cmd.Contains(normalizedExpected)))
                                    {
                                        isMatch = true;
                                    }
                                }
                            }
                            catch { }
                        }

                        if (!isMatch) continue;

                        var processId = process.Id;
                        process.Kill();
                        try { process.WaitForExit(ExitWaitMs); }
                        catch { }
                        terminatedProcessIds.Add(processId);
                    }
                    catch
                    {
                        // The process may exit between enumeration and path
                        // inspection. Application shutdown must still finish.
                    }
                }
            }
        }

        private static string GetProcessExecutablePath(Process process)
        {
            if (process == null) return null;

            if (OperatingSystem.IsMacOS())
            {
                var ptr = Marshal.AllocHGlobal(4096);
                try
                {
                    var len = 0;
                    try { len = proc_pidpath(process.Id, ptr, 4096); }
                    catch { len = proc_pidpath_fallback(process.Id, ptr, 4096); }

                    if (len > 0)
                    {
                        return Marshal.PtrToStringUTF8(ptr, len);
                    }
                }
                catch { }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            if (OperatingSystem.IsLinux())
            {
                try
                {
                    var procExe = $"/proc/{process.Id}/exe";
                    if (File.Exists(procExe))
                    {
                        var linkTarget = File.ResolveLinkTarget(procExe, true);
                        return linkTarget?.FullName ?? procExe;
                    }
                }
                catch { }
            }

            try
            {
                if (process.MainModule != null && !string.IsNullOrWhiteSpace(process.MainModule.FileName))
                    return process.MainModule.FileName;
            }
            catch { }

            return null;
        }

        private static string NormalizePathForComparison(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var norm = Path.GetFullPath(path);
            if (OperatingSystem.IsMacOS())
            {
                if (norm.StartsWith("/private/", StringComparison.Ordinal))
                    norm = norm.Substring(8);
            }
            return norm;
        }
    }
}
