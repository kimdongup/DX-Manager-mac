using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DexManager.Models;
using DexManager.Platform;
using DexManager.Utils;

namespace DexManager.Services
{
    public sealed class PathService
    {
        private readonly SettingsService _settingsService;
        private readonly LogService _logService;
        private readonly ProcessRunner _processRunner;
        private readonly IPathProvider _pathProvider;
        private readonly IPlatformService _platformService;

        public PathService(
            SettingsService settingsService,
            LogService logService,
            ProcessRunner processRunner,
            IPathProvider pathProvider = null,
            IPlatformService platformService = null)
        {
            _settingsService = settingsService;
            _logService = logService;
            _processRunner = processRunner;
            _pathProvider = pathProvider;
            _platformService = platformService;
        }

        public string SelectAdbPath(AppSettings settings, int timeoutMs)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            _logService.Info(LocalizationService.Format(
                "Log.Path.WindowsDetected",
                PlatformHelper.GetDisplayName(),
                PlatformHelper.CurrentVersion));

            if (settings.Paths.AdbSelectionMode == AdbSelectionMode.Manual)
            {
                return SelectRequired(
                    settings.Paths.AdbPath,
                    LocalizationService.Get(
                        "Path.Description.ManualAdb"),
                    timeoutMs);
            }

            if (PlatformHelper.RequiresLegacyAdb)
            {
                return SelectRequired(
                    settings.Paths.Win7AdbPath,
                    LocalizationService.Get(
                        "Path.Description.LegacyAdb"),
                    timeoutMs);
            }

            return SelectScrcpyAdbWithLegacyFallback(
                settings,
                timeoutMs);
        }

        public bool IsAdbDirectoryInProcessPath(string adbPath)
        {
            if (string.IsNullOrWhiteSpace(adbPath)) return false;
            var adbDirectory = NormalizeDirectory(Path.GetDirectoryName(adbPath));
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            return ContainsDirectory(pathValue, adbDirectory);
        }

        public bool IsAdbDirectoryInSystemPath(string adbPath)
        {
            if (string.IsNullOrWhiteSpace(adbPath)) return false;
            if (_platformService != null)
                return _platformService.IsDirectoryInSystemPath(Path.GetDirectoryName(adbPath));

            return IsAdbDirectoryInProcessPath(adbPath);
        }

        public bool TryRegisterAdbDirectoryInSystemPath(string adbPath)
        {
            if (string.IsNullOrWhiteSpace(adbPath)) return false;
            if (_platformService != null)
                return _platformService.TryRegisterDirectoryInSystemPath(Path.GetDirectoryName(adbPath));

            if (!AdminHelper.IsAdministrator())
            {
                _logService.Warning(LocalizationService.Get(
                    "Log.Path.AdminRequired"));
                return false;
            }

            return false;
        }

        private string SelectScrcpyAdbWithLegacyFallback(
            AppSettings settings,
            int timeoutMs)
        {
            var selected = GetScrcpyAdb(settings, timeoutMs);
            if (selected == null && _pathProvider != null)
            {
                foreach (var candidatePath in _pathProvider.GetCandidateAdbPaths())
                {
                    selected = GetRunnableCandidate(
                        candidatePath,
                        "System/Platform ADB",
                        timeoutMs,
                        true);
                    if (selected != null) break;
                }
            }

            if (selected == null)
            {
                selected = GetRunnableCandidate(
                    settings.Paths.Win7AdbPath,
                    LocalizationService.Get(
                        "Path.Description.LegacyAdb"),
                    timeoutMs);
            }

            if (selected == null)
            {
                // Fallback to checking PATH
                var systemAdb = FindExecutableInPath("adb");
                if (!string.IsNullOrWhiteSpace(systemAdb))
                {
                    selected = GetRunnableCandidate(
                        systemAdb,
                        "PATH ADB",
                        timeoutMs,
                        true);
                }
            }

            if (selected == null)
            {
                throw new FileNotFoundException(
                    LocalizationService.Get(
                        "Error.Path.AutomaticAdbNotFound"));
            }

            LogSelection(
                selected,
                LocalizationService.Get("Path.Mode.Automatic"));
            return selected.Path;
        }

        private string SelectRequired(
            string configuredPath,
            string description,
            int timeoutMs)
        {
            var candidate = GetRunnableCandidate(
                configuredPath,
                description,
                timeoutMs);
            if (candidate == null)
            {
                throw new FileNotFoundException(
                    LocalizationService.Format(
                        "Error.Path.AdbUnavailable",
                        description));
            }

            LogSelection(
                candidate,
                LocalizationService.Get("Path.Mode.Selected"));
            return candidate.Path;
        }

        private AdbPathCandidate GetScrcpyAdb(
            AppSettings settings,
            int timeoutMs)
        {
            var configuredScrcpy = _settingsService.ResolvePath(
                settings.Paths.ScrcpyPath);
            if (string.IsNullOrWhiteSpace(configuredScrcpy)) return null;

            var dir = Path.GetDirectoryName(configuredScrcpy);
            if (string.IsNullOrWhiteSpace(dir)) return null;

            var adbExecutableName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
            var adbPath = Path.Combine(dir, adbExecutableName);
            return GetRunnableCandidate(
                adbPath,
                LocalizationService.Get(
                    "Path.Description.ScrcpyAdb"),
                timeoutMs,
                true);
        }

        private AdbPathCandidate GetRunnableCandidate(
            string configuredPath,
            string description,
            int timeoutMs,
            bool pathIsAbsolute = false)
        {
            if (string.IsNullOrWhiteSpace(configuredPath)) return null;

            var path = pathIsAbsolute
                ? Path.GetFullPath(configuredPath)
                : _settingsService.ResolvePath(configuredPath);
            if (!File.Exists(path))
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.Path.CandidateMissing",
                    description,
                    path));
                return null;
            }

            try
            {
                var result = _processRunner.Run(
                    path,
                    "version",
                    Path.GetDirectoryName(path),
                    Math.Max(timeoutMs, 3000),
                    false,
                    Encoding.Default);
                if (!result.IsSuccess)
                {
                    _logService.Warning(LocalizationService.Format(
                        "Log.Path.CandidateExecutionFailed",
                        description,
                        result.StandardError));
                    return null;
                }

                return new AdbPathCandidate(
                    path,
                    description,
                    AdbVersionParser.GetDisplayVersion(
                        result.StandardOutput,
                        LocalizationService.Get(
                            "Path.VersionUnavailable")),
                    AdbVersionParser.GetVersionNumber(
                        result.StandardOutput));
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.Path.CandidateUnavailable",
                    description,
                    ex.Message));
                return null;
            }
        }

        private void LogSelection(AdbPathCandidate candidate, string settingsModeLabel)
        {
            _logService.Info(LocalizationService.Format(
                "Log.Path.Selection",
                settingsModeLabel,
                candidate.Description));
            _logService.Info(LocalizationService.Format(
                "Log.Path.SelectedAdbPath",
                candidate.Path));
            _logService.Info(LocalizationService.Format(
                "Log.Path.AdbVersion",
                candidate.VersionText));
        }

        private static string NormalizeDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return string.Empty;
            return Path.GetFullPath(directory.Trim().Trim('"'))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool ContainsDirectory(string pathValue, string directory)
        {
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (var entry in (pathValue ?? string.Empty).Split(
                new[] { separator },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(
                    NormalizeDirectory(entry),
                    directory,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FindExecutableInPath(string executableName)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv)) return null;

            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            var extensions = OperatingSystem.IsWindows()
                ? new[] { ".exe", ".cmd", ".bat", "" }
                : new[] { "" };

            foreach (var dir in pathEnv.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var ext in extensions)
                {
                    try
                    {
                        var full = Path.Combine(dir.Trim(), executableName + ext);
                        if (File.Exists(full)) return Path.GetFullPath(full);
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private sealed class AdbPathCandidate
        {
            public AdbPathCandidate(
                string path,
                string description,
                string versionText,
                Version version)
            {
                Path = path;
                Description = description;
                VersionText = versionText;
                Version = version;
            }

            public string Path { get; private set; }
            public string Description { get; private set; }
            public string VersionText { get; private set; }
            public Version Version { get; private set; }
        }
    }
}
