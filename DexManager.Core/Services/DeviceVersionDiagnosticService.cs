using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DexManager.Models;

namespace DexManager.Services
{
    public sealed class DeviceVersionDiagnosticService
    {
        private static readonly Regex PropertyLine = new Regex(
            @"^\[([^\]]+)\]: \[(.*)\]$",
            RegexOptions.Compiled);

        private readonly AdbService _adbService;

        public DeviceVersionDiagnosticService(AdbService adbService)
        {
            _adbService = adbService ??
                throw new ArgumentNullException("adbService");
        }

        public DeviceVersionDiagnostic Inspect(
            string serial,
            PhysicalDeviceInfo device)
        {
            var result = new DeviceVersionDiagnostic
            {
                Serial = serial ?? string.Empty,
                DisplayName = device == null
                    ? string.Empty
                    : device.DisplayName ?? string.Empty,
                TransportKind = FindTransportKind(device, serial),
                Compatibility = DeviceCompatibilityAssessment.Unknown
            };

            if (string.IsNullOrWhiteSpace(serial))
            {
                result.ErrorDetail = "No connected device is selected.";
                return result;
            }

            var command = _adbService.ShellForSerial(
                serial,
                "getprop",
                false);
            if (!command.IsSuccess)
            {
                result.ErrorDetail = command.TimedOut
                    ? "ADB device query timed out."
                    : command.Canceled
                        ? "ADB device query was canceled."
                        : command.StandardError;
                return result;
            }

            var properties = ParseProperties(command.StandardOutput);
            result.Model = FirstValue(
                properties,
                "ro.product.marketname",
                "ro.product.vendor.marketname",
                "ro.product.model");
            if (string.IsNullOrWhiteSpace(result.DisplayName))
                result.DisplayName = result.Model;
            result.AndroidVersion = Value(
                properties,
                "ro.build.version.release");
            int sdk;
            if (int.TryParse(
                Value(properties, "ro.build.version.sdk"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out sdk))
            {
                result.AndroidSdk = sdk;
            }
            result.OneUiVersion = ReadOneUiVersion(properties);
            result.SecurityPatch = Value(
                properties,
                "ro.build.version.security_patch");
            result.QuerySucceeded = true;
            result.Compatibility = AssessCompatibility(
                result.AndroidSdk,
                result.OneUiVersion);
            return result;
        }

        internal static IDictionary<string, string> ParseProperties(
            string output)
        {
            var properties = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var lines = (output ?? string.Empty).Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = PropertyLine.Match(line.Trim());
                if (!match.Success) continue;
                properties[match.Groups[1].Value] =
                    match.Groups[2].Value.Trim();
            }
            return properties;
        }

        internal static string ReadOneUiVersion(
            IDictionary<string, string> properties)
        {
            var explicitValue = Value(
                properties,
                "ro.build.version.oneui");
            var formatted = FormatEncodedOneUi(explicitValue, false);
            if (!string.IsNullOrWhiteSpace(formatted)) return formatted;

            return FormatEncodedOneUi(
                Value(properties, "ro.build.version.sem"),
                true);
        }

        private static DeviceCompatibilityAssessment AssessCompatibility(
            int sdk,
            string oneUiVersion)
        {
            Version version;
            if (!Version.TryParse(oneUiVersion, out version))
            {
                return sdk > 36
                    ? DeviceCompatibilityAssessment.NewerUnverified
                    : sdk > 0 && sdk < 36
                        ? DeviceCompatibilityAssessment.OlderUnverified
                        : DeviceCompatibilityAssessment.Unknown;
            }

            if (version.Major < 8 || sdk > 0 && sdk < 36)
                return DeviceCompatibilityAssessment.OlderUnverified;
            if (version.Major > 8 || sdk > 36)
                return DeviceCompatibilityAssessment.NewerUnverified;
            return DeviceCompatibilityAssessment.RecommendedBaseline;
        }

        private static string FormatEncodedOneUi(
            string raw,
            bool semValue)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            Version directVersion;
            if (raw.IndexOf('.') >= 0 &&
                Version.TryParse(raw, out directVersion))
            {
                return directVersion.Minor > 0
                    ? directVersion.Major + "." + directVersion.Minor
                    : directVersion.Major + ".0";
            }

            int encoded;
            if (!int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out encoded))
            {
                return string.Empty;
            }

            if (semValue) encoded -= 90000;
            if (encoded < 0) return string.Empty;

            var major = encoded / 10000;
            var remainder = encoded % 10000;
            var minor = remainder / 100;
            return major.ToString(CultureInfo.InvariantCulture) + "." +
                minor.ToString(CultureInfo.InvariantCulture);
        }

        private static DeviceTransportKind FindTransportKind(
            PhysicalDeviceInfo device,
            string serial)
        {
            var transport = device == null
                ? null
                : device.FindTransport(serial);
            return transport == null
                ? DeviceTransportKind.Unknown
                : transport.Kind;
        }

        private static string FirstValue(
            IDictionary<string, string> properties,
            params string[] names)
        {
            foreach (var name in names)
            {
                var value = Value(properties, name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        private static string Value(
            IDictionary<string, string> properties,
            string name)
        {
            if (properties == null || string.IsNullOrWhiteSpace(name))
                return string.Empty;
            string value;
            return properties.TryGetValue(name, out value)
                ? value ?? string.Empty
                : string.Empty;
        }
    }
}
