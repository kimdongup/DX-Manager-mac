using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DexManager.FileTransfer;
using DexManager.Models;

namespace DexManager.Services
{
    public sealed partial class FileTransferCoordinator : IDisposable
    {
        private AdbExecutionResult RunAdbPush(TransferWorkItem item)
        {
            var arguments = WindowsCommandLine.Build(new[]
            {
                "-s",
                item.Session.Serial,
                "push",
                item.CurrentEntry.LocalPath,
                item.RemoteTemporaryPath
            });
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _realAdbPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(_realAdbPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            })
            {
                var output = new BoundedTextBuffer();
                var error = new BoundedTextBuffer();
                process.Start();
                SetActiveProcess(item, process);
                var stdoutThread = StartReaderThread(
                    process.StandardOutput,
                    output,
                    null);
                var stderrThread = StartReaderThread(
                    process.StandardError,
                    error,
                    null);

                while (!process.WaitForExit(ProcessPollMilliseconds))
                {
                    if (item.IsCanceled ||
                        Interlocked.CompareExchange(
                            ref _shutdownRequested,
                            0,
                            0) != 0)
                    {
                        TryKill(process);
                    }
                }
                stdoutThread.Join(2000);
                stderrThread.Join(2000);
                ClearActiveProcess(process);
                return new AdbExecutionResult(
                    process.ExitCode,
                    output.Value,
                    error.Value);
            }
        }

        private Thread StartReaderThread(
            StreamReader reader,
            BoundedTextBuffer buffer,
            Action<int> progress)
        {
            var thread = new Thread(new ThreadStart(delegate
            {
                var chunk = new char[512];
                var progressTail = string.Empty;
                try
                {
                    int read;
                    while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        var text = new string(chunk, 0, read);
                        buffer.Append(text);
                        if (progress != null)
                        {
                            var progressText = progressTail + text;
                            var consumed = 0;
                            foreach (Match match in Regex.Matches(
                                progressText,
                                @"(?<!\d)(\d{1,3})%"))
                            {
                                int value;
                                if (int.TryParse(
                                    match.Groups[1].Value,
                                    NumberStyles.Integer,
                                    CultureInfo.InvariantCulture,
                                    out value) && value >= 0 && value <= 100)
                                {
                                    progress(value);
                                }
                                consumed = match.Index + match.Length;
                            }
                            progressTail = consumed > 0
                                ? progressText.Substring(consumed)
                                : progressText;
                            if (progressTail.Length > 16)
                                progressTail = progressTail.Substring(
                                    progressTail.Length - 16);
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }))
            {
                IsBackground = true,
                Name = "DX Manager ADB output reader"
            };
            thread.Start();
            return thread;
        }

        private AdbExecutionResult RunShellScript(
            TransferWorkItem item,
            string script,
            int timeoutMs)
        {
            return RunShellScript(item, script, timeoutMs, false);
        }

        private AdbExecutionResult RunShellScript(
            TransferWorkItem item,
            string script,
            int timeoutMs,
            bool ignoreCancellation)
        {
            var arguments = WindowsCommandLine.Build(new[]
            {
                "-s", item.Session.Serial, "shell", "sh"
            });
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _realAdbPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(_realAdbPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            })
            {
                process.Start();
                SetActiveProcess(item, process);
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                var stopwatch = Stopwatch.StartNew();
                var inputThread = StartStandardInputWriter(process, script);
                while (!process.WaitForExit(ProcessPollMilliseconds))
                {
                    var shutdownRequested = Interlocked.CompareExchange(
                        ref _shutdownRequested,
                        0,
                        0) != 0;
                    if ((!ignoreCancellation &&
                         (item.IsCanceled || shutdownRequested)) ||
                        stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        TryKill(process);
                    }
                }
                inputThread.Join(1000);
                ClearActiveProcess(process);
                return new AdbExecutionResult(
                    process.ExitCode,
                    GetTaskResult(output),
                    GetTaskResult(error));
            }
        }

        private static Thread StartStandardInputWriter(
            Process process,
            string script)
        {
            var thread = new Thread(delegate()
            {
                try
                {
                    var bytes = Encoding.ASCII.GetBytes(
                        (script ?? string.Empty) + "\n");
                    process.StandardInput.BaseStream.Write(
                        bytes,
                        0,
                        bytes.Length);
                    process.StandardInput.BaseStream.Flush();
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    try { process.StandardInput.Close(); }
                    catch { }
                }
            })
            {
                IsBackground = true,
                Name = "DX Manager ADB input writer"
            };
            thread.Start();
            return thread;
        }

        private AdbExecutionResult RunCleanupScript(
            string serial,
            StringBuilder script)
        {
            return RunCleanupScript(serial, script == null
                ? string.Empty
                : script.ToString());
        }

        private AdbExecutionResult RunCleanupScript(
            string serial,
            string script)
        {
            return RunCleanupScript(serial, script, ShortAdbTimeoutMs);
        }

        private AdbExecutionResult RunCleanupScript(
            string serial,
            string script,
            int timeoutMs)
        {
            var arguments = WindowsCommandLine.Build(new[]
            {
                "-s", serial, "shell", "sh"
            });
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _realAdbPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(_realAdbPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            })
            {
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                var inputThread = StartStandardInputWriter(process, script);
                var timedOut = !process.WaitForExit(timeoutMs);
                if (timedOut)
                {
                    TryKill(process);
                    process.WaitForExit(1000);
                }
                inputThread.Join(1000);
                if (!process.HasExited)
                {
                    return new AdbExecutionResult(
                        -1,
                        string.Empty,
                        string.Empty);
                }
                return new AdbExecutionResult(
                    timedOut ? -1 : process.ExitCode,
                    GetTaskResult(output),
                    GetTaskResult(error));
            }
        }

    }
}
