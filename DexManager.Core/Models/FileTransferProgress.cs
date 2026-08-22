using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DexManager.Models
{
    public sealed class FileTransferQueueEntry
    {
        internal FileTransferQueueEntry(
            string displayName,
            long fileSize,
            bool active)
        {
            DisplayName = displayName ?? string.Empty;
            FileSize = fileSize;
            Active = active;
        }

        public string DisplayName { get; private set; }
        public long FileSize { get; private set; }
        public bool Active { get; private set; }
    }

    public enum FileTransferStage
    {
        Queued = 0,
        Transferring = 1,
        Finalizing = 2,
        Completed = 3,
        Failed = 4,
        Canceled = 5
    }

    public sealed class FileTransferProgress
    {
        internal FileTransferProgress(
            long sequence,
            string requestId,
            string sessionId,
            FileTransferStage stage,
            string fileName,
            string finalFileName,
            long fileSize,
            int percent,
            int completedCount,
            int failedCount,
            int queuedCount,
            IEnumerable<FileTransferQueueEntry> visibleQueue,
            DateTime startedUtc,
            bool directoryTransfer,
            int batchItemCount,
            string message)
        {
            Sequence = sequence;
            RequestId = requestId ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            Stage = stage;
            FileName = fileName ?? string.Empty;
            FinalFileName = finalFileName ?? string.Empty;
            FileSize = fileSize;
            Percent = percent;
            CompletedCount = completedCount;
            FailedCount = failedCount;
            QueuedCount = queuedCount;
            VisibleQueue = new ReadOnlyCollection<FileTransferQueueEntry>(
                new List<FileTransferQueueEntry>(
                    visibleQueue ?? new FileTransferQueueEntry[0]));
            StartedUtc = startedUtc;
            DirectoryTransfer = directoryTransfer;
            BatchItemCount = batchItemCount;
            Message = message ?? string.Empty;
        }

        public long Sequence { get; private set; }
        public string RequestId { get; private set; }
        public string SessionId { get; private set; }
        public FileTransferStage Stage { get; private set; }
        public string FileName { get; private set; }
        public string FinalFileName { get; private set; }
        public long FileSize { get; private set; }
        public int Percent { get; private set; }
        public int CompletedCount { get; private set; }
        public int FailedCount { get; private set; }
        public int QueuedCount { get; private set; }
        public ReadOnlyCollection<FileTransferQueueEntry> VisibleQueue
        {
            get;
            private set;
        }
        public DateTime StartedUtc { get; private set; }
        public bool DirectoryTransfer { get; private set; }
        public int BatchItemCount { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class FileTransferProgressEventArgs : EventArgs
    {
        internal FileTransferProgressEventArgs(FileTransferProgress progress)
        {
            Progress = progress;
        }

        public FileTransferProgress Progress { get; private set; }
    }
}
