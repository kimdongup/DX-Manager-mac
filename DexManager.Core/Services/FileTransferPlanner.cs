using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DexManager.Services
{
    internal static class FileTransferPlanner
    {
        internal static bool TryCreateDirectoryPlan(
            string localPath,
            int maximumFileNameBytes,
            Func<bool> cancellationRequested,
            Action<string> reparsePointSkipped,
            out FileTransferPlan plan,
            out string error)
        {
            plan = null;
            error = string.Empty;
            try
            {
                var root = new DirectoryInfo(localPath);
                if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    error = LocalizationService.Get(
                        "FileTransfer.ReparsePointUnsupported");
                    return false;
                }

                string validationError;
                if (!TryValidatePathComponent(
                    root.Name,
                    maximumFileNameBytes,
                    out validationError))
                {
                    error = validationError;
                    return false;
                }

                var entries = new List<TransferEntry>();
                var stack = new Stack<DirectoryScanItem>();
                stack.Push(new DirectoryScanItem(root, string.Empty));
                long totalSize = 0;
                while (stack.Count > 0)
                {
                    if (IsCancellationRequested(cancellationRequested))
                    {
                        error = LocalizationService.Get(
                            "FileTransfer.CanceledByUser");
                        return false;
                    }

                    var current = stack.Pop();
                    var children = current.Directory.GetFileSystemInfos();
                    Array.Sort(children, delegate(
                        FileSystemInfo left,
                        FileSystemInfo right)
                    {
                        return StringComparer.OrdinalIgnoreCase.Compare(
                            left.Name,
                            right.Name);
                    });

                    for (var index = children.Length - 1;
                        index >= 0;
                        index--)
                    {
                        if ((index & 63) == 0 &&
                            IsCancellationRequested(cancellationRequested))
                        {
                            error = LocalizationService.Get(
                                "FileTransfer.CanceledByUser");
                            return false;
                        }

                        var child = children[index];
                        if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            if (reparsePointSkipped != null)
                                reparsePointSkipped(child.FullName);
                            continue;
                        }
                        if (!TryValidatePathComponent(
                            child.Name,
                            maximumFileNameBytes,
                            out validationError))
                        {
                            error = validationError;
                            return false;
                        }

                        var relativePath = string.IsNullOrEmpty(
                            current.RelativePath)
                            ? child.Name
                            : current.RelativePath + "/" + child.Name;
                        var directory = child as DirectoryInfo;
                        if (directory != null)
                        {
                            entries.Add(new TransferEntry(
                                directory.FullName,
                                relativePath,
                                root.Name + "/" + relativePath,
                                0L,
                                true));
                            stack.Push(new DirectoryScanItem(
                                directory,
                                relativePath));
                            continue;
                        }

                        var file = child as FileInfo;
                        if (file == null) continue;
                        var fileSize = file.Length;
                        totalSize = totalSize > long.MaxValue - fileSize
                            ? long.MaxValue
                            : totalSize + fileSize;
                        entries.Add(new TransferEntry(
                            file.FullName,
                            relativePath,
                            root.Name + "/" + relativePath,
                            fileSize,
                            false));
                    }
                }

                entries.Sort(delegate(TransferEntry left, TransferEntry right)
                {
                    if (left.IsDirectory != right.IsDirectory)
                        return left.IsDirectory ? -1 : 1;
                    return StringComparer.OrdinalIgnoreCase.Compare(
                        left.RelativePath,
                        right.RelativePath);
                });
                plan = new FileTransferPlan(
                    root.Name,
                    totalSize,
                    entries,
                    FindNextFileIndex(entries, 0));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryValidatePathComponent(
            string value,
            int maximumFileNameBytes,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, ".", StringComparison.Ordinal) ||
                string.Equals(value, "..", StringComparison.Ordinal))
            {
                error = LocalizationService.Get(
                    "FileTransfer.InvalidFileName");
                return false;
            }
            if (Encoding.UTF8.GetByteCount(value) > maximumFileNameBytes)
            {
                error = LocalizationService.Format(
                    "FileTransfer.FileNameTooLong",
                    maximumFileNameBytes);
                return false;
            }
            return true;
        }

        private static bool IsCancellationRequested(
            Func<bool> cancellationRequested)
        {
            return cancellationRequested != null &&
                cancellationRequested();
        }

        private static int FindNextFileIndex(
            IList<TransferEntry> entries,
            int startIndex)
        {
            for (var index = Math.Max(startIndex, 0);
                index < entries.Count;
                index++)
            {
                if (!entries[index].IsDirectory) return index;
            }
            return entries.Count;
        }

        private sealed class DirectoryScanItem
        {
            internal DirectoryScanItem(
                DirectoryInfo directory,
                string relativePath)
            {
                Directory = directory;
                RelativePath = relativePath;
            }

            internal DirectoryInfo Directory { get; private set; }
            internal string RelativePath { get; private set; }
        }
    }

    internal sealed class FileTransferPlan
    {
        internal FileTransferPlan(
            string rootName,
            long totalSize,
            List<TransferEntry> entries,
            int firstFileIndex)
        {
            RootName = rootName;
            TotalSize = totalSize;
            Entries = entries;
            FirstFileIndex = firstFileIndex;
        }

        internal string RootName { get; private set; }
        internal long TotalSize { get; private set; }
        internal List<TransferEntry> Entries { get; private set; }
        internal int FirstFileIndex { get; private set; }
    }

    internal sealed class TransferEntry
    {
        internal TransferEntry(
            string localPath,
            string relativePath,
            string displayName,
            long fileSize,
            bool isDirectory)
        {
            LocalPath = localPath;
            RelativePath = relativePath;
            DisplayName = displayName;
            FileSize = fileSize;
            IsDirectory = isDirectory;
        }

        internal string LocalPath { get; private set; }
        internal string RelativePath { get; private set; }
        internal string DisplayName { get; private set; }
        internal long FileSize { get; private set; }
        internal bool IsDirectory { get; private set; }
    }
}
