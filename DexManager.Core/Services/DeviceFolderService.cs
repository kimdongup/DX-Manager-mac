using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DexManager.Services
{
    internal sealed class DeviceFolderService
    {
        private const string DisplayRoot = "/sdcard";
        private const string PhysicalRoot = "/storage/emulated/0";
        private readonly AdbService _adbService;

        public DeviceFolderService(AdbService adbService)
        {
            _adbService = adbService;
        }

        public IList<string> ListFolders(string serial, string folder)
        {
            var displayFolder = NormalizeDisplayPath(folder);
            var physicalFolder = ToPhysicalPath(displayFolder);
            var command = "find " + ShellQuote(physicalFolder) +
                " -mindepth 1 -maxdepth 1 -type d -print0 | base64";
            var result = _adbService.ShellForSerial(
                serial,
                command,
                false);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? result.StandardOutput
                        : result.StandardError);
            }

            var encoded = result.StandardOutput ?? string.Empty;
            var bytes = encoded.Trim().Length == 0
                ? new byte[0]
                : Convert.FromBase64String(encoded);
            var decoded = Encoding.UTF8.GetString(bytes);
            var folders = decoded.Split(
                new[] { '\0' },
                StringSplitOptions.RemoveEmptyEntries)
                .Select(ToDisplayPath)
                .Where(IsSharedStoragePath)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(GetName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return folders;
        }

        public static string NormalizeDisplayPath(string value)
        {
            var normalized = (value ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .TrimEnd('/');
            while (normalized.Contains("//"))
                normalized = normalized.Replace("//", "/");

            if (string.Equals(
                normalized,
                PhysicalRoot,
                StringComparison.Ordinal))
            {
                return DisplayRoot;
            }
            if (normalized.StartsWith(
                PhysicalRoot + "/",
                StringComparison.Ordinal))
            {
                return DisplayRoot +
                    normalized.Substring(PhysicalRoot.Length);
            }
            return IsSharedStoragePath(normalized)
                ? normalized
                : DisplayRoot;
        }

        public static string GetParent(string folder)
        {
            var normalized = NormalizeDisplayPath(folder);
            if (string.Equals(
                normalized,
                DisplayRoot,
                StringComparison.Ordinal))
            {
                return DisplayRoot;
            }
            var separator = normalized.LastIndexOf('/');
            return separator <= DisplayRoot.Length
                ? DisplayRoot
                : normalized.Substring(0, separator);
        }

        public static string GetName(string folder)
        {
            var normalized = NormalizeDisplayPath(folder);
            if (string.Equals(
                normalized,
                DisplayRoot,
                StringComparison.Ordinal))
            {
                return DisplayRoot;
            }
            var separator = normalized.LastIndexOf('/');
            return separator < 0
                ? normalized
                : normalized.Substring(separator + 1);
        }

        private static string ToPhysicalPath(string displayPath)
        {
            return PhysicalRoot +
                displayPath.Substring(DisplayRoot.Length);
        }

        private static string ToDisplayPath(string physicalPath)
        {
            var normalized = (physicalPath ?? string.Empty)
                .Trim()
                .TrimEnd('/');
            return normalized.StartsWith(
                PhysicalRoot,
                StringComparison.Ordinal)
                ? DisplayRoot + normalized.Substring(PhysicalRoot.Length)
                : normalized;
        }

        private static bool IsSharedStoragePath(string path)
        {
            return string.Equals(
                    path,
                    DisplayRoot,
                    StringComparison.Ordinal) ||
                path.StartsWith(
                    DisplayRoot + "/",
                    StringComparison.Ordinal);
        }

        private static string ShellQuote(string value)
        {
            return "'" + (value ?? string.Empty)
                .Replace("'", "'\\''") + "'";
        }
    }
}
