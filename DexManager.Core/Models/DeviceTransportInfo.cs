using System;

namespace DexManager.Models
{
    public enum DeviceTransportKind
    {
        Unknown = 0,
        Usb = 1,
        Wireless = 2,
        Emulator = 3
    }

    public sealed class DeviceTransportInfo
    {
        public string Serial { get; set; }
        public DeviceTransportKind Kind { get; set; }
        public AdbDeviceStatus Status { get; set; }
        public string RawStatus { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public bool IsAuthorized
        {
            get { return Status == AdbDeviceStatus.Device; }
        }

        public DeviceTransportInfo Clone()
        {
            return new DeviceTransportInfo
            {
                Serial = Serial,
                Kind = Kind,
                Status = Status,
                RawStatus = RawStatus,
                LastSeenUtc = LastSeenUtc
            };
        }
    }

    public sealed class DiscoveredDeviceTransport
    {
        public string DeviceIdentity { get; set; }
        public string DisplayName { get; set; }
        public string Serial { get; set; }
        public DeviceTransportKind Kind { get; set; }
        public AdbDeviceStatus Status { get; set; }
        public string RawStatus { get; set; }
    }
}
