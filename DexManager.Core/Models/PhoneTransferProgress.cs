using System;

namespace DexManager.Models
{
    public enum PhoneTransferStage
    {
        Receiving = 0,
        Completed = 1,
        Failed = 2,
        Canceled = 3
    }

    public sealed class PhoneTransferProgress
    {
        internal PhoneTransferProgress(
            long sequence,
            Guid batchId,
            PhoneTransferStage stage,
            string currentItem,
            int completedItems,
            int totalItems,
            long receivedBytes,
            long totalBytes,
            string destinationFolder,
            string error)
        {
            Sequence = sequence;
            BatchId = batchId;
            Stage = stage;
            CurrentItem = currentItem ?? string.Empty;
            CompletedItems = completedItems;
            TotalItems = totalItems;
            ReceivedBytes = receivedBytes;
            TotalBytes = totalBytes;
            DestinationFolder = destinationFolder ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public long Sequence { get; private set; }
        public Guid BatchId { get; private set; }
        public PhoneTransferStage Stage { get; private set; }
        public string CurrentItem { get; private set; }
        public int CompletedItems { get; private set; }
        public int TotalItems { get; private set; }
        public long ReceivedBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public string DestinationFolder { get; private set; }
        public string Error { get; private set; }
    }

    public sealed class PhoneTransferProgressEventArgs : EventArgs
    {
        internal PhoneTransferProgressEventArgs(PhoneTransferProgress progress)
        {
            Progress = progress;
        }

        public PhoneTransferProgress Progress { get; private set; }
    }
}
