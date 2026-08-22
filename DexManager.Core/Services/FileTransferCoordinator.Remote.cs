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
        private bool TryCreateRemoteStagingDirectory(
            TransferWorkItem item,
            out string error)
        {
            var script = new StringBuilder();
            script.AppendLine("set -e");
            AppendDecodedVariable(
                script,
                "base",
                item.Session.RemoteDirectory);
            AppendDecodedVariable(
                script,
                "staging",
                item.RemoteStagingDirectory);
            script.AppendLine("mkdir -p \"$base\"");
            script.AppendLine("[ -d \"$base\" ]");
            script.AppendLine("[ ! -e \"$staging\" ]");
            script.AppendLine("mkdir \"$staging\"");
            return TryRunPreparationScript(item, script.ToString(), out error);
        }

        private bool TryCreateRemoteSubdirectories(
            TransferWorkItem item,
            out string error)
        {
            var directories = item.Entries
                .Where(entry => entry.IsDirectory)
                .ToArray();
            if (directories.Length == 0)
            {
                error = string.Empty;
                return true;
            }

            var script = new StringBuilder();
            script.AppendLine("set -e");
            AppendDecodedVariable(
                script,
                "root",
                item.RemoteStagingDirectory);
            foreach (var directory in directories)
            {
                AppendDecodedVariable(
                    script,
                    "rel",
                    directory.RelativePath);
                script.AppendLine("mkdir -p \"$root/$rel\"");
            }
            var timeout = Math.Min(
                60000,
                Math.Max(ShortAdbTimeoutMs, directories.Length * 50));
            var result = RunShellScript(item, script.ToString(), timeout);
            if (item.IsCanceled)
            {
                error = LocalizationService.Get(
                    "FileTransfer.CanceledByUser");
                return false;
            }
            if (result.ExitCode == 0)
            {
                error = string.Empty;
                return true;
            }
            error = string.IsNullOrWhiteSpace(result.ErrorTail)
                ? LocalizationService.Get("FileTransfer.FolderCreateFailed")
                : result.ErrorTail;
            return false;
        }

        private bool TryMoveTemporaryFileIntoStaging(
            TransferWorkItem item,
            TransferEntry entry,
            out string error)
        {
            var script = new StringBuilder();
            script.AppendLine("set -e");
            AppendDecodedVariable(
                script,
                "root",
                item.RemoteStagingDirectory);
            AppendDecodedVariable(script, "rel", entry.RelativePath);
            AppendDecodedVariable(
                script,
                "tmp",
                item.RemoteTemporaryPath);
            script.AppendLine("dest=\"$root/$rel\"");
            script.AppendLine("parent=${dest%/*}");
            script.AppendLine("mkdir -p \"$parent\"");
            script.AppendLine("[ ! -e \"$dest\" ]");
            script.AppendLine("mv \"$tmp\" \"$dest\"");
            script.AppendLine("[ ! -e \"$tmp\" ]");
            var result = RunShellScript(
                item,
                script.ToString(),
                ShortAdbTimeoutMs);
            if (item.IsCanceled)
            {
                error = LocalizationService.Get(
                    "FileTransfer.CanceledByUser");
                return false;
            }
            if (result.ExitCode == 0)
            {
                error = string.Empty;
                return true;
            }
            error = string.IsNullOrWhiteSpace(result.ErrorTail)
                ? LocalizationService.Get("FileTransfer.RenameFailed")
                : result.ErrorTail;
            return false;
        }

        private bool TryRunPreparationScript(
            TransferWorkItem item,
            string script,
            out string error)
        {
            var result = RunShellScript(item, script, ShortAdbTimeoutMs);
            if (item.IsCanceled)
            {
                error = LocalizationService.Get(
                    "FileTransfer.CanceledByUser");
                return false;
            }
            if (result.ExitCode == 0)
            {
                error = string.Empty;
                return true;
            }
            error = string.IsNullOrWhiteSpace(result.ErrorTail)
                ? LocalizationService.Get("FileTransfer.FolderCreateFailed")
                : result.ErrorTail;
            return false;
        }

        private static void AppendDecodedVariable(
            StringBuilder builder,
            string variable,
            string value)
        {
            builder.Append(variable)
                .Append("=\"$(printf '%s' '")
                .Append(ToBase64(value))
                .AppendLine("' | base64 -d)\"");
        }

        private bool TryFinalizeRemoteFile(
            TransferWorkItem item,
            out string finalFileName,
            out string error)
        {
            finalFileName = string.Empty;
            error = string.Empty;
            var name = item.FileName;
            var extension = Path.GetExtension(name) ?? string.Empty;
            var stem = extension.Length == 0
                ? name
                : name.Substring(0, name.Length - extension.Length);
            string collisionStem;
            string collisionExtension;
            PrepareCollisionNameParts(
                name,
                stem,
                extension,
                out collisionStem,
                out collisionExtension);
            if (!TryBeginFinalCommit(item, out error)) return false;
            item.RemoteCommitMarkerPath = "/data/local/tmp/.dxm-commit-" +
                Guid.NewGuid().ToString("N") + ".result";
            item.FinalCommitRecoveryPending = true;
            var script = BuildFinalizeScript(
                item.Session.RemoteDirectory,
                item.RemoteTemporaryPath,
                item.RemoteCommitMarkerPath,
                name,
                collisionStem,
                collisionExtension);
            try
            {
                var result = RunShellScript(
                    item,
                    script,
                    ShortAdbTimeoutMs,
                    true);
                result = RecoverFinalCommitResult(item, result);
                item.FinalCommitRecoveryPending = !HasFinalCommitResult(result);
                return TryReadCollisionResult(
                    item,
                    result,
                    name,
                    collisionStem,
                    collisionExtension,
                    "FileTransfer.RenameFailed",
                    out finalFileName,
                    out error);
            }
            finally
            {
                EndFinalCommit(item);
                if (!item.FinalCommitRecoveryPending)
                    CleanupRemoteCommitMarker(item);
            }
        }

        private bool TryFinalizeRemoteDirectory(
            TransferWorkItem item,
            out string finalDirectoryName,
            out string error)
        {
            var name = item.RootName;
            string collisionStem;
            string collisionExtension;
            PrepareCollisionNameParts(
                name,
                name,
                string.Empty,
                out collisionStem,
                out collisionExtension);
            if (!TryBeginFinalCommit(item, out error))
            {
                finalDirectoryName = string.Empty;
                return false;
            }
            item.RemoteCommitMarkerPath = "/data/local/tmp/.dxm-commit-" +
                Guid.NewGuid().ToString("N") + ".result";
            item.FinalCommitRecoveryPending = true;
            var script = BuildFinalizeScript(
                item.Session.RemoteDirectory,
                item.RemoteStagingDirectory,
                item.RemoteCommitMarkerPath,
                name,
                collisionStem,
                collisionExtension);
            try
            {
                var result = RunShellScript(
                    item,
                    script,
                    ShortAdbTimeoutMs,
                    true);
                result = RecoverFinalCommitResult(item, result);
                item.FinalCommitRecoveryPending = !HasFinalCommitResult(result);
                return TryReadCollisionResult(
                    item,
                    result,
                    name,
                    collisionStem,
                    collisionExtension,
                    "FileTransfer.FolderFinalizeFailed",
                    out finalDirectoryName,
                    out error);
            }
            finally
            {
                EndFinalCommit(item);
                if (!item.FinalCommitRecoveryPending)
                    CleanupRemoteCommitMarker(item);
            }
        }

        private bool TryBeginFinalCommit(
            TransferWorkItem item,
            out string error)
        {
            lock (_syncRoot)
            {
                if (item.IsCanceled ||
                    !item.Session.Active ||
                    Interlocked.CompareExchange(
                        ref _shutdownRequested,
                        0,
                        0) != 0)
                {
                    error = LocalizationService.Get(
                        "FileTransfer.CanceledByUser");
                    return false;
                }
                item.BeginCommit();
            }
            error = string.Empty;
            return true;
        }

        private void EndFinalCommit(TransferWorkItem item)
        {
            lock (_syncRoot) item.EndCommit();
        }

        private AdbExecutionResult RecoverFinalCommitResult(
            TransferWorkItem item,
            AdbExecutionResult originalResult)
        {
            if (Regex.IsMatch(
                    originalResult.OutputTail ?? string.Empty,
                    @"DXM_INDEX=(\d+)",
                    RegexOptions.CultureInvariant) ||
                string.IsNullOrWhiteSpace(item.RemoteCommitMarkerPath))
            {
                return originalResult;
            }

            try
            {
                var temporaryPath = item.DirectoryTransfer
                    ? item.RemoteStagingDirectory
                    : item.RemoteTemporaryPath;
                if (string.IsNullOrWhiteSpace(temporaryPath))
                    return originalResult;

                var script = new StringBuilder();
                script.AppendLine("set -e");
                AppendDecodedVariable(
                    script,
                    "marker",
                    item.RemoteCommitMarkerPath);
                AppendDecodedVariable(script, "tmp", temporaryPath);
                script.AppendLine("attempt=0");
                script.Append("while [ \"$attempt\" -lt ")
                    .Append(FinalCommitRecoveryAttempts.ToString(
                        CultureInfo.InvariantCulture))
                    .AppendLine(" ]; do");
                script.AppendLine("  if [ -f \"$marker\" ]; then");
                script.AppendLine("    record=\"$(cat \"$marker\")\"");
                script.AppendLine("    index=\"\"");
                script.AppendLine("    case \"$record\" in");
                script.AppendLine("      C:*) index=\"${record#C:}\" ;;");
                script.AppendLine("      P:*)");
                script.AppendLine("        if [ ! -e \"$tmp\" ]; then");
                script.AppendLine("          index=\"${record#P:}\"");
                script.AppendLine("        fi");
                script.AppendLine("        ;;");
                script.AppendLine("    esac");
                script.AppendLine("    case \"$index\" in");
                script.AppendLine("      ''|*[!0-9]*) ;;");
                script.AppendLine("      *) printf 'DXM_INDEX=%s\\n' \"$index\"; exit 0 ;;");
                script.AppendLine("    esac");
                script.AppendLine("  fi");
                script.AppendLine("  attempt=$((attempt + 1))");
                script.Append("  [ \"$attempt\" -ge ")
                    .Append(FinalCommitRecoveryAttempts.ToString(
                        CultureInfo.InvariantCulture))
                    .AppendLine(" ] || sleep 1");
                script.AppendLine("done");
                script.AppendLine("exit 72");
                var recovered = RunCleanupScript(
                    item.Session.Serial,
                    script.ToString(),
                    (FinalCommitRecoveryAttempts * 1000) + 2000);
                if (!Regex.IsMatch(
                    recovered.OutputTail ?? string.Empty,
                    @"DXM_INDEX=(\d+)",
                    RegexOptions.CultureInvariant))
                {
                    return originalResult;
                }

                _logService.Info(LocalizationService.Format(
                    "Log.FileTransfer.CommitRecovered",
                    item.FileName));
                return recovered;
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.FileTransfer.CommitRecoveryFailed",
                    ex.Message));
                return originalResult;
            }
        }

        private static bool HasFinalCommitResult(AdbExecutionResult result)
        {
            return result != null && Regex.IsMatch(
                result.OutputTail ?? string.Empty,
                @"DXM_INDEX=(\d+)",
                RegexOptions.CultureInvariant);
        }

        private static bool TryReadCollisionResult(
            TransferWorkItem item,
            AdbExecutionResult result,
            string name,
            string collisionStem,
            string collisionExtension,
            string failureResource,
            out string finalName,
            out string error)
        {
            finalName = string.Empty;
            error = string.Empty;
            var match = Regex.Match(
                result.OutputTail ?? string.Empty,
                @"DXM_INDEX=(\d+)",
                RegexOptions.CultureInvariant);
            if (match.Success)
            {
                int collisionIndex;
                if (!int.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out collisionIndex) ||
                    collisionIndex < 0 ||
                    collisionIndex > MaximumCollisionIndex)
                {
                    error = LocalizationService.Get(
                        "FileTransfer.RenameResultInvalid");
                    return false;
                }
                finalName = collisionIndex == 0
                    ? name
                    : collisionStem + " (" + collisionIndex.ToString(
                        CultureInfo.InvariantCulture) + ")" +
                        collisionExtension;
                if (Encoding.UTF8.GetByteCount(finalName) >
                    MaximumFileNameBytes)
                {
                    error = LocalizationService.Format(
                        "FileTransfer.FileNameTooLong",
                        MaximumFileNameBytes);
                    return false;
                }
                return true;
            }

            if (item.IsCanceled)
            {
                error = LocalizationService.Get(
                    "FileTransfer.CanceledByUser");
                return false;
            }
            if (result.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(result.ErrorTail)
                    ? LocalizationService.Get(failureResource)
                    : result.ErrorTail;
                return false;
            }

            error = LocalizationService.Get(
                "FileTransfer.RenameResultMissing");
            return false;
        }

        private static void PrepareCollisionNameParts(
            string name,
            string stem,
            string extension,
            out string collisionStem,
            out string collisionExtension)
        {
            var suffixBytes = Encoding.UTF8.GetByteCount(
                " (" + MaximumCollisionIndex.ToString(
                    CultureInfo.InvariantCulture) + ")");
            var stemBudget = MaximumFileNameBytes - suffixBytes -
                Encoding.UTF8.GetByteCount(extension ?? string.Empty);
            if (stemBudget > 0)
            {
                collisionStem = TruncateUtf8(stem, stemBudget);
                if (!string.IsNullOrEmpty(collisionStem))
                {
                    collisionExtension = extension ?? string.Empty;
                    return;
                }
            }

            collisionExtension = string.Empty;
            collisionStem = TruncateUtf8(
                name,
                MaximumFileNameBytes - suffixBytes);
        }

        private static string TruncateUtf8(string value, int maximumBytes)
        {
            if (string.IsNullOrEmpty(value) || maximumBytes <= 0)
                return string.Empty;
            if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
                return value;

            var builder = new StringBuilder();
            var usedBytes = 0;
            var elements = StringInfo.GetTextElementEnumerator(value);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                var elementBytes = Encoding.UTF8.GetByteCount(element);
                if (usedBytes + elementBytes > maximumBytes) break;
                builder.Append(element);
                usedBytes += elementBytes;
            }
            return builder.ToString();
        }

        private static string BuildFinalizeScript(
            string directory,
            string temporaryPath,
            string markerPath,
            string name,
            string stem,
            string extension)
        {
            var builder = new StringBuilder();
            builder.AppendLine("set -e");
            AppendDecodedVariable(builder, "dir", directory);
            AppendDecodedVariable(builder, "tmp", temporaryPath);
            AppendDecodedVariable(builder, "marker", markerPath);
            AppendDecodedVariable(builder, "name", name);
            AppendDecodedVariable(builder, "stem", stem);
            AppendDecodedVariable(builder, "ext", extension);
            builder.AppendLine("mkdir -p \"$dir\"");
            builder.AppendLine("[ -d \"$dir\" ] || exit 51");
            builder.AppendLine("rm -f \"$marker\"");
            builder.AppendLine("candidate=\"$name\"");
            builder.AppendLine("index=0");
            builder.AppendLine("while :; do");
            builder.AppendLine("  if [ ! -e \"$dir/$candidate\" ]; then");
            builder.AppendLine("    printf 'P:%s\\n' \"$index\" > \"$marker\"");
            builder.AppendLine("    mv -n \"$tmp\" \"$dir/$candidate\"");
            builder.AppendLine("    if [ ! -e \"$tmp\" ]; then");
            builder.AppendLine("      printf 'C:%s\\n' \"$index\" > \"$marker\"");
            builder.AppendLine("      printf 'DXM_INDEX=%s\\n' \"$index\"");
            builder.AppendLine("      exit 0");
            builder.AppendLine("    fi");
            builder.AppendLine("  fi");
            builder.AppendLine("  index=$((index + 1))");
            builder.Append("  [ \"$index\" -le ")
                .Append(MaximumCollisionIndex.ToString(
                    CultureInfo.InvariantCulture))
                .AppendLine(" ] || exit 52");
            builder.AppendLine("  candidate=\"$stem ($index)$ext\"");
            builder.AppendLine("done");
            return builder.ToString();
        }

        private void CleanupRemoteCommitMarker(TransferWorkItem item)
        {
            if (item.FinalCommitRecoveryPending) return;
            if (string.IsNullOrWhiteSpace(item.RemoteCommitMarkerPath)) return;
            var markerPath = item.RemoteCommitMarkerPath;
            var cleaned = false;
            try
            {
                var script = new StringBuilder();
                AppendDecodedVariable(script, "path", markerPath);
                script.AppendLine("case \"$path\" in");
                script.AppendLine(
                    "  /data/local/tmp/.dxm-commit-*.result) rm -f \"$path\" ;;");
                script.AppendLine("  *) exit 61 ;;");
                script.AppendLine("esac");
                var result = RunCleanupScript(
                    item.Session.Serial,
                    script.ToString());
                if (result.ExitCode != 0)
                {
                    _logService.Warning(LocalizationService.Format(
                        "Log.FileTransfer.TempCleanupFailed",
                        GetCleanupFailure(markerPath, result)));
                }
                else
                {
                    cleaned = true;
                }
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.FileTransfer.TempCleanupFailed",
                    ex.Message));
            }
            finally
            {
                if (cleaned) item.RemoteCommitMarkerPath = string.Empty;
            }
        }

        private void CleanupRemoteTemporaryFile(TransferWorkItem item)
        {
            if (item.FinalCommitRecoveryPending) return;
            if (string.IsNullOrWhiteSpace(item.RemoteTemporaryPath)) return;
            try
            {
                var script = new StringBuilder();
                AppendDecodedVariable(
                    script,
                    "path",
                    item.RemoteTemporaryPath);
                script.AppendLine("rm -f \"$path\"");
                var result = RunCleanupScript(item.Session.Serial, script);
                if (result.ExitCode == 0)
                {
                    item.RemoteTemporaryPath = string.Empty;
                }
                else
                {
                    _logService.Warning(LocalizationService.Format(
                        "Log.FileTransfer.TempCleanupFailed",
                        GetCleanupFailure(item.RemoteTemporaryPath, result)));
                }
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.FileTransfer.TempCleanupFailed",
                    ex.Message));
            }
        }

        private void CleanupRemoteTransferArtifacts(TransferWorkItem item)
        {
            if (item.FinalCommitRecoveryPending) return;
            CleanupRemoteCommitMarker(item);
            CleanupRemoteTemporaryFile(item);
            if (string.IsNullOrWhiteSpace(item.RemoteStagingDirectory)) return;
            try
            {
                var script = new StringBuilder();
                AppendDecodedVariable(
                    script,
                    "path",
                    item.RemoteStagingDirectory);
                script.AppendLine("case \"$path\" in");
                script.AppendLine("  */.dxm-dir-*.part) rm -rf \"$path\" ;;");
                script.AppendLine("  *) exit 61 ;;");
                script.AppendLine("esac");
                var result = RunCleanupScript(
                    item.Session.Serial,
                    script.ToString());
                if (result.ExitCode == 0)
                {
                    item.RemoteStagingDirectory = string.Empty;
                }
                else
                {
                    _logService.Warning(LocalizationService.Format(
                        "Log.FileTransfer.TempCleanupFailed",
                        GetCleanupFailure(
                            item.RemoteStagingDirectory,
                            result)));
                }
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.FileTransfer.TempCleanupFailed",
                    ex.Message));
            }
        }

        private static string GetCleanupFailure(
            string path,
            AdbExecutionResult result)
        {
            var detail = string.IsNullOrWhiteSpace(result.ErrorTail)
                ? result.OutputTail
                : result.ErrorTail;
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = "ADB exit code " + result.ExitCode.ToString(
                    CultureInfo.InvariantCulture);
            }
            return (path ?? string.Empty) + ": " + detail;
        }

    }
}
