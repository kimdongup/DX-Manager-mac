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
        private void CancelSessionRequests(
            string sessionId,
            bool setBurst)
        {
            TransferWorkItem[] requests;
            lock (_syncRoot)
            {
                if (setBurst)
                {
                    TransferSession session;
                    if (_sessions.TryGetValue(
                        sessionId ?? string.Empty,
                        out session))
                    {
                        session.CancelBurstUntilUtc = DateTime.UtcNow
                            .AddMilliseconds(CancelBurstMilliseconds);
                    }
                }
                requests = _requests.Values
                    .Where(item => string.Equals(
                        item.Request.SessionId,
                        sessionId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            foreach (var item in requests) CancelItem(item);
        }

        private void CancelItem(TransferWorkItem item)
        {
            if (item == null) return;
            Process process = null;
            lock (_syncRoot)
            {
                if (item.IsTerminal) return;
                item.Cancel();
                if (ReferenceEquals(_activeItem, item) &&
                    !item.IsCommitInProgress)
                {
                    process = _activeAdbProcess;
                }
            }
            if (process != null) TryKill(process);
        }

        private void SetActiveProcess(
            TransferWorkItem item,
            Process process)
        {
            var shouldKill = false;
            lock (_syncRoot)
            {
                if (ReferenceEquals(_activeItem, item))
                    _activeAdbProcess = process;
                shouldKill = item.IsCanceled &&
                    !item.IsCommitInProgress;
            }
            if (shouldKill) TryKill(process);
        }

        private void ClearActiveProcess(Process process)
        {
            lock (_syncRoot)
            {
                if (ReferenceEquals(_activeAdbProcess, process))
                    _activeAdbProcess = null;
            }
        }

        private static void TryKill(Process process)
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static bool IsManagedRemoteDirectory(
            string value,
            string expected)
        {
            return string.Equals(
                NormalizeRemoteDirectory(value),
                NormalizeRemoteDirectory(expected),
                StringComparison.Ordinal);
        }

        private static string NormalizeRemoteDirectory(string value)
        {
            var normalized = (value ?? string.Empty)
                .Trim()
                .Replace('\\', '/');
            while (normalized.Contains("//"))
                normalized = normalized.Replace("//", "/");
            normalized = normalized.TrimEnd('/');
            if (!normalized.StartsWith(
                    "/sdcard/",
                    StringComparison.Ordinal) &&
                !normalized.StartsWith(
                    "/storage/emulated/0/",
                    StringComparison.Ordinal))
            {
                normalized = FileTransferEnvironment
                    .DefaultRemoteDirectory
                    .TrimEnd('/');
            }
            return normalized;
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
            var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
            var difference = leftBytes.Length ^ rightBytes.Length;
            var length = Math.Max(leftBytes.Length, rightBytes.Length);
            for (var index = 0; index < length; index++)
            {
                var leftValue = index < leftBytes.Length
                    ? leftBytes[index]
                    : (byte)0;
                var rightValue = index < rightBytes.Length
                    ? rightBytes[index]
                    : (byte)0;
                difference |= leftValue ^ rightValue;
            }
            return difference == 0;
        }

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static string ToBase64(string value)
        {
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string FormatBytes(long bytes)
        {
            var value = (double)Math.Max(bytes, 0L);
            var units = new[] { "B", "KB", "MB", "GB", "TB" };
            var unit = 0;
            while (value >= 1024D && unit < units.Length - 1)
            {
                value /= 1024D;
                unit++;
            }
            return value.ToString(
                unit == 0 ? "0" : "0.##",
                CultureInfo.InvariantCulture) + " " + units[unit];
        }

        private static string GetTaskResult(
            System.Threading.Tasks.Task<string> task)
        {
            try { return task.GetAwaiter().GetResult(); }
            catch { return string.Empty; }
        }

        private sealed class TransferSession
        {
            internal TransferSession(
                string id,
                string serial,
                string displayName,
                string remoteDirectory)
            {
                Id = id;
                Serial = serial;
                DisplayName = displayName ?? string.Empty;
                RemoteDirectory = NormalizeRemoteDirectory(remoteDirectory);
                Active = true;
            }

            internal string Id { get; private set; }
            internal string Serial { get; private set; }
            internal string DisplayName { get; private set; }
            internal string RemoteDirectory { get; private set; }
            internal bool Active { get; set; }
            internal int ProcessId { get; set; }
            internal IntPtr WindowHandle { get; set; }
            internal int CompletedCount { get; set; }
            internal int FailedCount { get; set; }
            internal DateTime CancelBurstUntilUtc { get; set; }
        }

        private sealed class TransferWorkItem
        {
            private int _canceled;
            private int _commitInProgress;
            private int _terminal;

            internal TransferWorkItem(
                FileTransferRequestMessage request,
                TransferSession session,
                NamedPipeServerStream pipe)
            {
                Request = request;
                Session = session;
                Pipe = pipe;
                DisconnectBuffer = new byte[1];
                Entries = new List<TransferEntry>();
                CurrentStage = FileTransferStage.Queued;
            }

            internal FileTransferRequestMessage Request { get; private set; }
            internal TransferSession Session { get; private set; }
            internal NamedPipeServerStream Pipe { get; private set; }
            internal byte[] DisconnectBuffer { get; private set; }
            internal string FileName { get; set; }
            internal string FinalFileName { get; set; }
            internal string RemoteCommitMarkerPath { get; set; }
            internal string RemoteTemporaryPath { get; set; }
            internal string RemoteStagingDirectory { get; set; }
            internal string RootName { get; set; }
            internal long FileSize { get; set; }
            internal long TotalSize { get; set; }
            internal bool RenameCompleted { get; set; }
            internal bool FinalCommitRecoveryPending { get; set; }
            internal bool DirectoryTransfer { get; set; }
            internal List<TransferEntry> Entries { get; set; }
            internal int CurrentEntryIndex { get; set; }
            internal FileTransferStage CurrentStage { get; set; }
            internal string CurrentMessage { get; set; }
            internal int CurrentPercent { get; set; }
            internal int CompletedEntryCount { get; set; }
            internal DateTime StartedUtc { get; set; }
            internal TransferEntry CurrentEntry
            {
                get
                {
                    return CurrentEntryIndex >= 0 &&
                        CurrentEntryIndex < Entries.Count
                        ? Entries[CurrentEntryIndex]
                        : null;
                }
            }
            internal bool IsCanceled
            {
                get { return Interlocked.CompareExchange(ref _canceled, 0, 0) != 0; }
            }
            internal bool IsCommitInProgress
            {
                get
                {
                    return Interlocked.CompareExchange(
                        ref _commitInProgress,
                        0,
                        0) != 0;
                }
            }
            internal bool IsTerminal
            {
                get { return Interlocked.CompareExchange(ref _terminal, 0, 0) != 0; }
            }

            internal void Cancel()
            {
                Interlocked.Exchange(ref _canceled, 1);
            }

            internal void BeginCommit()
            {
                Interlocked.Exchange(ref _commitInProgress, 1);
            }

            internal void EndCommit()
            {
                Interlocked.Exchange(ref _commitInProgress, 0);
            }

            internal void MarkTerminal()
            {
                Interlocked.Exchange(ref _terminal, 1);
            }
        }

    }
}
