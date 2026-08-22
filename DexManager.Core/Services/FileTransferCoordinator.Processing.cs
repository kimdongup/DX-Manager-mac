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
        private void WorkerLoop()
        {
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    lock (_syncRoot) _activeItem = item;
                    try { ProcessItem(item); }
                    catch (Exception ex)
                    {
                        CleanupRemoteTransferArtifacts(item);
                        if (item.IsCanceled) CompleteCanceled(item);
                        else CompleteFailed(item, ex.Message);
                    }
                    finally
                    {
                        lock (_syncRoot)
                        {
                            if (ReferenceEquals(_activeItem, item))
                                _activeItem = null;
                            _activeAdbProcess = null;
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ProcessItem(TransferWorkItem item)
        {
            if (item.IsCanceled || !item.Session.Active)
            {
                CompleteCanceled(item);
                return;
            }

            item.StartedUtc = DateTime.UtcNow;
            if (Directory.Exists(item.Request.LocalPath))
            {
                ProcessDirectory(item);
                return;
            }
            ProcessSingleFile(item);
        }

        private void ProcessSingleFile(TransferWorkItem item)
        {
            FileInfo file;
            try { file = new FileInfo(item.Request.LocalPath); }
            catch (Exception ex)
            {
                CompleteFailed(item, ex.Message);
                return;
            }

            var fileName = file.Name;
            string validationError;
            if (!FileTransferPlanner.TryValidatePathComponent(
                fileName,
                MaximumFileNameBytes,
                out validationError))
            {
                CompleteFailed(item, validationError);
                return;
            }

            item.DirectoryTransfer = false;
            item.RootName = fileName;
            item.FileName = fileName;
            item.FileSize = file.Length;
            item.TotalSize = file.Length;
            item.Entries = new List<TransferEntry>
            {
                new TransferEntry(
                    file.FullName,
                    fileName,
                    fileName,
                    file.Length,
                    false)
            };
            item.CurrentEntryIndex = 0;

            _logService.Info(LocalizationService.Format(
                "Log.FileTransfer.Starting",
                fileName,
                FormatBytes(file.Length),
                item.Session.Serial));

            string error;
            if (!TryTransferCurrentFile(item, false, out error))
            {
                CleanupRemoteTemporaryFile(item);
                if (item.IsCanceled) CompleteCanceled(item);
                else CompleteFailed(item, error);
                return;
            }

            lock (_syncRoot) item.Session.CompletedCount++;
            item.CurrentEntryIndex = item.Entries.Count;
            CompleteSuccess(item);
        }

        private void ProcessDirectory(TransferWorkItem item)
        {
            string error;
            if (!TryPrepareDirectoryTransfer(item, out error))
            {
                if (item.IsCanceled) CompleteCanceled(item);
                else CompleteFailed(item, error);
                return;
            }

            _logService.Info(LocalizationService.Format(
                "Log.FileTransfer.Starting",
                item.RootName,
                FormatBytes(item.TotalSize),
                item.Session.Serial));

            Publish(item, FileTransferStage.Queued, -1, string.Empty);
            if (!TryCreateRemoteStagingDirectory(item, out error) ||
                !TryCreateRemoteSubdirectories(item, out error))
            {
                CleanupRemoteTransferArtifacts(item);
                if (item.IsCanceled) CompleteCanceled(item);
                else CompleteFailed(item, error);
                return;
            }

            for (var index = 0; index < item.Entries.Count; index++)
            {
                var entry = item.Entries[index];
                if (entry.IsDirectory) continue;
                item.CurrentEntryIndex = index;
                item.FileName = entry.DisplayName;
                item.FileSize = entry.FileSize;
                if (!TryTransferCurrentFile(item, true, out error))
                {
                    CleanupRemoteTransferArtifacts(item);
                    if (item.IsCanceled) CompleteCanceled(item);
                    else CompleteFailed(item, error);
                    return;
                }
                item.CompletedEntryCount++;
                item.CurrentEntryIndex = index + 1;
            }

            item.FileName = item.RootName;
            item.FileSize = item.TotalSize;
            Publish(item, FileTransferStage.Finalizing, -1, string.Empty);
            string finalDirectoryName;
            if (!TryFinalizeRemoteDirectory(
                item,
                out finalDirectoryName,
                out error))
            {
                CleanupRemoteTransferArtifacts(item);
                if (item.IsCanceled) CompleteCanceled(item);
                else CompleteFailed(item, error);
                return;
            }

            item.RemoteStagingDirectory = string.Empty;
            item.FinalFileName = finalDirectoryName;
            var committedCount = Math.Max(1, item.CompletedEntryCount);
            lock (_syncRoot)
            {
                item.Session.CompletedCount += committedCount;
            }
            item.CompletedEntryCount = 0;
            item.CurrentEntryIndex = item.Entries.Count;
            CompleteSuccess(item);
        }

        private bool TryTransferCurrentFile(
            TransferWorkItem item,
            bool intoStagingDirectory,
            out string error)
        {
            error = string.Empty;
            var entry = item.CurrentEntry;
            if (entry == null || entry.IsDirectory ||
                !File.Exists(entry.LocalPath))
            {
                error = LocalizationService.Get(
                    "FileTransfer.SourceUnavailable");
                return false;
            }

            item.RemoteTemporaryPath = "/sdcard/.dxm-file-" +
                Guid.NewGuid().ToString("N") + ".part";
            item.RenameCompleted = false;
            Publish(item, FileTransferStage.Transferring, -1, string.Empty);

            var pushResult = RunAdbPush(item);
            if (item.IsCanceled)
            {
                error = LocalizationService.Get(
                    "FileTransfer.CanceledByUser");
                return false;
            }
            if (pushResult.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(pushResult.ErrorTail)
                    ? LocalizationService.Get("FileTransfer.PushFailed")
                    : pushResult.ErrorTail;
                return false;
            }

            Publish(item, FileTransferStage.Finalizing, -1, string.Empty);
            if (intoStagingDirectory)
            {
                if (!TryMoveTemporaryFileIntoStaging(item, entry, out error))
                    return false;
                item.RemoteTemporaryPath = string.Empty;
                item.RenameCompleted = true;
                return true;
            }

            string finalFileName;
            if (!TryFinalizeRemoteFile(item, out finalFileName, out error))
                return false;
            item.RemoteTemporaryPath = string.Empty;
            item.RenameCompleted = true;
            item.FinalFileName = finalFileName;
            return true;
        }

        private bool TryPrepareDirectoryTransfer(
            TransferWorkItem item,
            out string error)
        {
            FileTransferPlan plan;
            if (!FileTransferPlanner.TryCreateDirectoryPlan(
                item.Request.LocalPath,
                MaximumFileNameBytes,
                delegate
                {
                    return item.IsCanceled ||
                        Interlocked.CompareExchange(
                            ref _shutdownRequested,
                            0,
                            0) != 0;
                },
                delegate(string path)
                {
                    _logService.Warning(LocalizationService.Format(
                        "Log.FileTransfer.ReparseSkipped",
                        path));
                },
                out plan,
                out error))
            {
                return false;
            }

            item.DirectoryTransfer = true;
            item.RootName = plan.RootName;
            item.FileName = plan.RootName;
            item.FileSize = plan.TotalSize;
            item.TotalSize = plan.TotalSize;
            item.Entries = plan.Entries;
            item.CurrentEntryIndex = plan.FirstFileIndex;
            item.RemoteStagingDirectory =
                item.Session.RemoteDirectory + "/.dxm-dir-" +
                Guid.NewGuid().ToString("N") + ".part";
            return true;
        }

    }
}
