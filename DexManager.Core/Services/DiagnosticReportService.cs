using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DexManager.Models;

namespace DexManager.Services
{
    public sealed class DiagnosticReportService
    {
        private static readonly Regex IpAddress = new Regex(
            @"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?",
            RegexOptions.Compiled);
        private static readonly Regex TokenValue = new Regex(
            @"(?i)(token|auth(?:entication)?)[=: ]+[^\s,;]+",
            RegexOptions.Compiled);
        private static readonly Regex QuotedPath = new Regex(
            "\"[^\"\r\n]*[\\\\/][^\"\r\n]*\"",
            RegexOptions.Compiled);
        private static readonly Regex AbsoluteWindowsPath = new Regex(
            @"(?i)(?<![a-z0-9_])(?:[a-z]:[\\/]|\\\\)[^\r\n]*",
            RegexOptions.Compiled);

        public string CreateReport(
            string appVersion,
            string adbPath,
            string adbVersion,
            string scrcpyPath,
            string selectedIdentity,
            DeviceRegistrySnapshot devices,
            DeviceRuntimeRegistrySnapshot runtimes,
            DeviceVersionDiagnostic diagnostic,
            DisplayCleanupPermissionStatus companion,
            IEnumerable<string> sessionEntries)
        {
            var builder = new StringBuilder();
            builder.AppendLine("DX Manager diagnostic report");
            builder.AppendLine("Generated: " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            builder.AppendLine("DX Manager: " + Safe(appVersion));
            builder.AppendLine("Windows: " + Environment.OSVersion);
            builder.AppendLine("64-bit OS: " +
                Environment.Is64BitOperatingSystem);
            builder.AppendLine(".NET runtime: " + Environment.Version);
            builder.AppendLine("ADB executable: " + FileName(adbPath));
            builder.AppendLine("ADB version: " + FormatAdbVersion(adbVersion));
            builder.AppendLine("Scrcpy executable: " + FileName(scrcpyPath));
            builder.AppendLine();

            builder.AppendLine("[Selected device]");
            AppendDevice(builder, diagnostic);
            builder.AppendLine("Identity: " + MaskIdentity(selectedIdentity));
            builder.AppendLine("Companion: " + CompanionText(companion));

            var runtime = FindRuntime(runtimes, selectedIdentity, diagnostic);
            if (runtime == null)
            {
                builder.AppendLine("Runtime session: not found");
            }
            else
            {
                AppendRuntime(builder, runtime);
            }
            builder.AppendLine();

            builder.AppendLine("[Connected devices]");
            if (devices == null || devices.Devices == null ||
                devices.Devices.Count == 0)
            {
                builder.AppendLine("None");
            }
            else
            {
                var deviceNumber = 0;
                foreach (var device in devices.Devices)
                {
                    if (device == null) continue;
                    deviceNumber++;
                    builder.Append("- Device ");
                    builder.Append(deviceNumber);
                    builder.Append(" | identity=");
                    builder.Append(MaskIdentity(device.Identity));
                    builder.Append(" | transports=");
                    builder.AppendLine(TransportSummary(device.Transports));
                }
            }
            builder.AppendLine();

            builder.AppendLine("[Recent warnings and errors]");
            var selectedSerial = diagnostic == null
                ? string.Empty
                : diagnostic.Serial;
            var sensitiveSerials = CollectSensitiveSerials(
                devices,
                selectedSerial);
            var relevant = (sessionEntries ?? Enumerable.Empty<string>())
                .Where(IsWarningOrError)
                .TakeLastCompatible(40)
                .Select(line => SanitizeLogLine(line, sensitiveSerials))
                .ToArray();
            if (relevant.Length == 0)
                builder.AppendLine("None in this session.");
            else
                foreach (var line in relevant) builder.AppendLine(line);

            builder.AppendLine();
            builder.AppendLine(
                "Device names, serials, IP addresses, authentication values, and local paths are masked.");
            return builder.ToString();
        }

        private static void AppendDevice(
            StringBuilder builder,
            DeviceVersionDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                builder.AppendLine("Version query: not run");
                return;
            }

            builder.AppendLine("Name: <device-name>");
            builder.AppendLine("Model: " + Safe(diagnostic.Model));
            builder.AppendLine("Transport: " + diagnostic.TransportKind);
            builder.AppendLine("Serial: " + MaskSerial(diagnostic.Serial));
            builder.AppendLine("Android: " + Safe(diagnostic.AndroidVersion) +
                (diagnostic.AndroidSdk > 0
                    ? " (SDK " + diagnostic.AndroidSdk + ")"
                    : string.Empty));
            builder.AppendLine("One UI: " + Safe(diagnostic.OneUiVersion));
            builder.AppendLine("Security patch: " +
                Safe(diagnostic.SecurityPatch));
            builder.AppendLine("Compatibility: " +
                diagnostic.Compatibility);
            if (!diagnostic.QuerySucceeded)
            {
                builder.AppendLine("Version query error: " +
                    OneLine(diagnostic.ErrorDetail));
            }
        }

        private static void AppendRuntime(
            StringBuilder builder,
            DeviceRuntimeSessionSnapshot runtime)
        {
            builder.AppendLine("Runtime connected: " + runtime.IsConnected);
            builder.AppendLine("DeX running: " +
                (runtime.Dex != null && runtime.Dex.IsRunning));
            var singleCount = runtime.SingleWindows == null
                ? 0
                : runtime.SingleWindows.Count(item =>
                    item != null && item.IsRunning);
            builder.AppendLine("Single-app windows running: " + singleCount);
            builder.AppendLine("Companion guardian attached: " +
                (runtime.Companion != null && runtime.Companion.IsAttached));
            if (runtime.Transfers != null)
            {
                builder.AppendLine("PC to phone transfer sessions: " +
                    runtime.Transfers.ActivePcToPhoneSessions);
                builder.AppendLine("PC to phone queued items: " +
                    runtime.Transfers.QueuedPcToPhoneItems);
                builder.AppendLine("Phone to PC transfers: active=" +
                    runtime.Transfers.ActivePhoneToPcTransfers);
            }
            if (runtime.PhonePower != null)
            {
                builder.AppendLine("Phone screen-off requested: " +
                    runtime.PhonePower.ScreenOffRequested);
                builder.AppendLine("Stay-awake override applied: " +
                    runtime.PhonePower.StayAwakeOverrideApplied);
            }
        }

        private static DeviceRuntimeSessionSnapshot FindRuntime(
            DeviceRuntimeRegistrySnapshot runtimes,
            string identity,
            DeviceVersionDiagnostic diagnostic)
        {
            if (runtimes == null) return null;
            var runtime = runtimes.FindByIdentity(identity);
            if (runtime != null) return runtime;
            return diagnostic == null
                ? null
                : runtimes.FindByTransportSerial(diagnostic.Serial);
        }

        private static string CompanionText(
            DisplayCleanupPermissionStatus status)
        {
            if (status == null) return "not inspected";
            return status.State +
                (status.VersionCode > 0
                    ? " (versionCode " + status.VersionCode + ")"
                    : string.Empty);
        }

        private static string TransportSummary(
            IList<DeviceTransportInfo> transports)
        {
            if (transports == null || transports.Count == 0) return "none";
            return string.Join(", ", transports
                .Where(item => item != null)
                .Select(item => item.Kind + ":" + item.Status + ":" +
                    MaskSerial(item.Serial)));
        }

        private static bool IsWarningOrError(string line)
        {
            return !string.IsNullOrWhiteSpace(line) &&
                (line.IndexOf("[WARN]", StringComparison.Ordinal) >= 0 ||
                 line.IndexOf("[ERROR]", StringComparison.Ordinal) >= 0);
        }

        private static string SanitizeLogLine(
            string line,
            IEnumerable<string> serials)
        {
            var result = line ?? string.Empty;
            foreach (var serial in serials ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(serial)) continue;
                result = result.Replace(serial, MaskSerial(serial));
            }
            var userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                result = result.Replace(userProfile, "<user-profile>");
            }
            result = IpAddress.Replace(result, "<ip>");
            result = TokenValue.Replace(result, "$1=<redacted>");
            result = QuotedPath.Replace(result, "\"<path>\"");
            result = AbsoluteWindowsPath.Replace(result, "<path>");
            return result;
        }

        private static string FormatAdbVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";

            var versionLines = value
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line =>
                    line.StartsWith(
                        "Android Debug Bridge version",
                        StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith(
                        "Version ",
                        StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (versionLines.Length > 0)
                return string.Join(" | ", versionLines);

            return SanitizeLogLine(OneLine(value), null);
        }

        private static IEnumerable<string> CollectSensitiveSerials(
            DeviceRegistrySnapshot devices,
            string selectedSerial)
        {
            var values = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(selectedSerial))
                values.Add(selectedSerial);
            if (devices != null && devices.Devices != null)
            {
                foreach (var device in devices.Devices)
                {
                    if (device == null || device.Transports == null) continue;
                    foreach (var transport in device.Transports)
                    {
                        if (transport != null &&
                            !string.IsNullOrWhiteSpace(transport.Serial))
                        {
                            values.Add(transport.Serial);
                        }
                    }
                }
            }
            return values.OrderByDescending(value => value.Length).ToArray();
        }

        private static string MaskIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return "<none>";
            var separator = identity.IndexOf(':');
            if (separator < 0) return MaskSerial(identity);
            return identity.Substring(0, separator + 1) +
                MaskSerial(identity.Substring(separator + 1));
        }

        private static string MaskSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return "<none>";
            var value = serial.Trim();
            if (value.Length <= 4) return new string('*', value.Length);
            return value.Substring(0, 2) + "***" +
                value.Substring(value.Length - 2);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        }

        private static string OneLine(string value)
        {
            return Safe(value).Replace("\r", " ").Replace("\n", " ");
        }

        private static string FileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "unknown";
            try { return Path.GetFileName(path); }
            catch { return "unknown"; }
        }
    }

    internal static class DiagnosticEnumerableExtensions
    {
        internal static IEnumerable<T> TakeLastCompatible<T>(
            this IEnumerable<T> source,
            int count)
        {
            if (source == null || count <= 0) return Enumerable.Empty<T>();
            var queue = new Queue<T>(count);
            foreach (var item in source)
            {
                if (queue.Count == count) queue.Dequeue();
                queue.Enqueue(item);
            }
            return queue.ToArray();
        }
    }
}
