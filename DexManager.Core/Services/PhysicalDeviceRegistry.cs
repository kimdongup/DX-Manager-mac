using System;
using System.Collections.Generic;
using DexManager.Models;

namespace DexManager.Services
{
    public sealed class PhysicalDeviceRegistry
    {
        private const string TemporaryIdentityPrefix = "transport:";
        private readonly object _sync = new object();
        private readonly Dictionary<string, string> _displayNameCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _identityByTransport =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private DeviceRegistrySnapshot _current = CreateEmptySnapshot();

        public event EventHandler<DeviceRegistrySnapshotChangedEventArgs>
            SnapshotChanged;

        public DeviceRegistrySnapshot Current
        {
            get
            {
                lock (_sync)
                {
                    return _current.Clone();
                }
            }
        }

        public DeviceRegistrySnapshot Reconcile(
            IEnumerable<DiscoveredDeviceTransport> observations)
        {
            var now = DateTime.UtcNow;
            var nextDevices = BuildDevices(observations, now);
            DeviceRegistrySnapshot previous;
            DeviceRegistrySnapshot current;
            var changed = false;

            lock (_sync)
            {
                previous = _current;
                changed = !SnapshotsEqual(previous.Devices, nextDevices);
                current = new DeviceRegistrySnapshot
                {
                    Generation = changed
                        ? previous.Generation + 1
                        : previous.Generation,
                    CapturedAtUtc = now,
                    Devices = nextDevices
                };
                _current = current;
            }

            if (changed)
            {
                var handler = SnapshotChanged;
                if (handler != null)
                {
                    handler(
                        this,
                        new DeviceRegistrySnapshotChangedEventArgs(
                            previous.Clone(),
                            current.Clone()));
                }
            }
            return current.Clone();
        }

        public DeviceRegistrySnapshot Reset()
        {
            return Reconcile(new DiscoveredDeviceTransport[0]);
        }

        public static string CreateTemporaryIdentity(string transportSerial)
        {
            if (string.IsNullOrWhiteSpace(transportSerial))
                return string.Empty;
            return TemporaryIdentityPrefix + transportSerial.Trim();
        }

        public static bool IsTemporaryIdentity(string identity)
        {
            return !string.IsNullOrWhiteSpace(identity) &&
                identity.Trim().StartsWith(
                    TemporaryIdentityPrefix,
                    StringComparison.OrdinalIgnoreCase);
        }

        private IList<PhysicalDeviceInfo> BuildDevices(
            IEnumerable<DiscoveredDeviceTransport> observations,
            DateTime now)
        {
            var bySerial = new Dictionary<string, DiscoveredDeviceTransport>(
                StringComparer.OrdinalIgnoreCase);
            if (observations != null)
            {
                foreach (var observation in observations)
                {
                    if (observation == null ||
                        string.IsNullOrWhiteSpace(observation.Serial))
                    {
                        continue;
                    }

                    var normalized = NormalizeObservation(observation);
                    DiscoveredDeviceTransport existing;
                    if (!bySerial.TryGetValue(normalized.Serial, out existing) ||
                        IsBetterObservation(normalized, existing))
                    {
                        bySerial[normalized.Serial] = normalized;
                    }
                }
            }

            var grouped = new Dictionary<
                string,
                List<DiscoveredDeviceTransport>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var observation in bySerial.Values)
            {
                var identity = ResolveIdentity(observation);
                List<DiscoveredDeviceTransport> group;
                if (!grouped.TryGetValue(identity, out group))
                {
                    group = new List<DiscoveredDeviceTransport>();
                    grouped.Add(identity, group);
                }
                group.Add(observation);
            }

            var devices = new List<PhysicalDeviceInfo>();
            foreach (var item in grouped)
            {
                var displayName = ResolveDisplayName(item.Key, item.Value);
                var device = new PhysicalDeviceInfo
                {
                    Identity = item.Key,
                    DisplayName = displayName
                };
                item.Value.Sort(CompareObservations);
                foreach (var observation in item.Value)
                {
                    device.Transports.Add(new DeviceTransportInfo
                    {
                        Serial = observation.Serial,
                        Kind = observation.Kind,
                        Status = observation.Status,
                        RawStatus = observation.RawStatus,
                        LastSeenUtc = now
                    });
                }
                devices.Add(device);
            }

            devices.Sort(CompareDevices);
            return devices;
        }

        private string ResolveIdentity(
            DiscoveredDeviceTransport observation)
        {
            if (!string.IsNullOrWhiteSpace(observation.DeviceIdentity))
            {
                lock (_sync)
                {
                    _identityByTransport[observation.Serial] =
                        observation.DeviceIdentity;
                }
                return observation.DeviceIdentity;
            }

            lock (_sync)
            {
                string knownIdentity;
                if (_identityByTransport.TryGetValue(
                        observation.Serial,
                        out knownIdentity) &&
                    !string.IsNullOrWhiteSpace(knownIdentity))
                {
                    return knownIdentity;
                }
            }

            return CreateTemporaryIdentity(observation.Serial);
        }

        private string ResolveDisplayName(
            string identity,
            IList<DiscoveredDeviceTransport> observations)
        {
            string displayName = null;
            foreach (var observation in observations)
            {
                if (!string.IsNullOrWhiteSpace(observation.DisplayName))
                {
                    displayName = observation.DisplayName.Trim();
                    break;
                }
            }

            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    _displayNameCache[identity] = displayName;
                    return displayName;
                }

                if (_displayNameCache.TryGetValue(identity, out displayName) &&
                    !string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }
            }

            return observations.Count > 0
                ? observations[0].Serial
                : identity;
        }

        private static DiscoveredDeviceTransport NormalizeObservation(
            DiscoveredDeviceTransport observation)
        {
            return new DiscoveredDeviceTransport
            {
                DeviceIdentity = string.IsNullOrWhiteSpace(
                    observation.DeviceIdentity)
                    ? string.Empty
                    : observation.DeviceIdentity.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(observation.DisplayName)
                    ? string.Empty
                    : observation.DisplayName.Trim(),
                Serial = observation.Serial.Trim(),
                Kind = observation.Kind,
                Status = observation.Status,
                RawStatus = string.IsNullOrWhiteSpace(observation.RawStatus)
                    ? string.Empty
                    : observation.RawStatus.Trim()
            };
        }

        private static bool IsBetterObservation(
            DiscoveredDeviceTransport candidate,
            DiscoveredDeviceTransport existing)
        {
            if (existing == null) return true;
            var candidateStable = !string.IsNullOrWhiteSpace(
                candidate.DeviceIdentity);
            var existingStable = !string.IsNullOrWhiteSpace(
                existing.DeviceIdentity);
            if (candidateStable != existingStable) return candidateStable;

            var candidateAuthorized =
                candidate.Status == AdbDeviceStatus.Device;
            var existingAuthorized =
                existing.Status == AdbDeviceStatus.Device;
            if (candidateAuthorized != existingAuthorized)
                return candidateAuthorized;

            return !string.IsNullOrWhiteSpace(candidate.DisplayName) &&
                string.IsNullOrWhiteSpace(existing.DisplayName);
        }

        private static bool SnapshotsEqual(
            IList<PhysicalDeviceInfo> left,
            IList<PhysicalDeviceInfo> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (!DevicesEqual(left[i], right[i])) return false;
            }
            return true;
        }

        private static bool DevicesEqual(
            PhysicalDeviceInfo left,
            PhysicalDeviceInfo right)
        {
            if (left == null || right == null) return left == right;
            if (!string.Equals(
                    left.Identity,
                    right.Identity,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    left.DisplayName,
                    right.DisplayName,
                    StringComparison.Ordinal) ||
                left.Transports == null ||
                right.Transports == null ||
                left.Transports.Count != right.Transports.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Transports.Count; i++)
            {
                var leftTransport = left.Transports[i];
                var rightTransport = right.Transports[i];
                if (leftTransport == null || rightTransport == null)
                {
                    if (leftTransport != rightTransport) return false;
                    continue;
                }
                if (!string.Equals(
                        leftTransport.Serial,
                        rightTransport.Serial,
                        StringComparison.OrdinalIgnoreCase) ||
                    leftTransport.Kind != rightTransport.Kind ||
                    leftTransport.Status != rightTransport.Status ||
                    !string.Equals(
                        leftTransport.RawStatus,
                        rightTransport.RawStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static int CompareObservations(
            DiscoveredDeviceTransport left,
            DiscoveredDeviceTransport right)
        {
            var result = GetTransportOrder(left.Kind).CompareTo(
                GetTransportOrder(right.Kind));
            if (result != 0) return result;
            return string.Compare(
                left.Serial,
                right.Serial,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareDevices(
            PhysicalDeviceInfo left,
            PhysicalDeviceInfo right)
        {
            var result = string.Compare(
                left == null ? string.Empty : left.DisplayName,
                right == null ? string.Empty : right.DisplayName,
                StringComparison.CurrentCultureIgnoreCase);
            if (result != 0) return result;
            return string.Compare(
                left == null ? string.Empty : left.Identity,
                right == null ? string.Empty : right.Identity,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int GetTransportOrder(DeviceTransportKind kind)
        {
            switch (kind)
            {
                case DeviceTransportKind.Usb:
                    return 0;
                case DeviceTransportKind.Wireless:
                    return 1;
                case DeviceTransportKind.Unknown:
                    return 2;
                case DeviceTransportKind.Emulator:
                    return 3;
                default:
                    return 4;
            }
        }

        private static DeviceRegistrySnapshot CreateEmptySnapshot()
        {
            return new DeviceRegistrySnapshot
            {
                Generation = 0,
                CapturedAtUtc = DateTime.UtcNow,
                Devices = new List<PhysicalDeviceInfo>()
            };
        }
    }
}
