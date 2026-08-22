using System;
using System.Collections.Generic;

namespace DexManager.Models
{
    public sealed class PhysicalDeviceInfo
    {
        public PhysicalDeviceInfo()
        {
            Transports = new List<DeviceTransportInfo>();
        }

        public string Identity { get; set; }
        public string DisplayName { get; set; }
        public IList<DeviceTransportInfo> Transports { get; set; }

        public bool IsConnected
        {
            get
            {
                if (Transports == null) return false;
                foreach (var transport in Transports)
                {
                    if (transport != null && transport.IsAuthorized)
                        return true;
                }
                return false;
            }
        }

        public DeviceTransportInfo FindTransport(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial) || Transports == null)
                return null;

            foreach (var transport in Transports)
            {
                if (transport != null &&
                    string.Equals(
                        transport.Serial,
                        serial.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return transport;
                }
            }
            return null;
        }

        public DeviceTransportInfo SelectPreferredTransport(
            string preferredSerial)
        {
            var preferred = FindTransport(preferredSerial);
            if (preferred != null && preferred.IsAuthorized)
                return preferred;

            var transport = FindFirstAuthorized(DeviceTransportKind.Usb);
            if (transport != null) return transport;

            transport = FindFirstAuthorized(DeviceTransportKind.Wireless);
            if (transport != null) return transport;

            transport = FindFirstAuthorized(DeviceTransportKind.Unknown);
            if (transport != null) return transport;

            transport = FindFirstAuthorized(DeviceTransportKind.Emulator);
            if (transport != null) return transport;

            if (preferred != null) return preferred;
            return Transports != null && Transports.Count > 0
                ? Transports[0]
                : null;
        }

        public DeviceTransportInfo SelectAuthorizedTransport(
            DeviceTransportKind requiredKind,
            string preferredSerial)
        {
            var preferred = FindTransport(preferredSerial);
            if (preferred != null &&
                preferred.Kind == requiredKind &&
                preferred.IsAuthorized)
            {
                return preferred;
            }

            return FindFirstAuthorized(requiredKind);
        }

        public PhysicalDeviceInfo Clone()
        {
            var copy = new PhysicalDeviceInfo
            {
                Identity = Identity,
                DisplayName = DisplayName
            };
            if (Transports != null)
            {
                foreach (var transport in Transports)
                {
                    if (transport != null)
                        copy.Transports.Add(transport.Clone());
                }
            }
            return copy;
        }

        private DeviceTransportInfo FindFirstAuthorized(
            DeviceTransportKind kind)
        {
            if (Transports == null) return null;
            foreach (var transport in Transports)
            {
                if (transport != null &&
                    transport.Kind == kind &&
                    transport.IsAuthorized)
                {
                    return transport;
                }
            }
            return null;
        }
    }
}
