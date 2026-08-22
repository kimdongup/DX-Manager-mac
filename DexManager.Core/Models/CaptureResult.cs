using System;

namespace DexManager.Models
{
    public sealed class CaptureResult
    {
        public CaptureResult(
            string localPath,
            string remotePath,
            bool transferredToDevice)
        {
            LocalPath = localPath;
            RemotePath = remotePath;
            TransferredToDevice = transferredToDevice;
        }

        public string LocalPath { get; private set; }
        public string RemotePath { get; private set; }
        public bool TransferredToDevice { get; private set; }
    }
}
