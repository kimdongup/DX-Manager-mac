using System;
using System.Collections.Generic;

namespace DexManager.Models
{
    public sealed class DeviceRegistrySnapshot
    {
        public DeviceRegistrySnapshot()
        {
            Devices = new List<PhysicalDeviceInfo>();
        }

        public long Generation { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public IList<PhysicalDeviceInfo> Devices { get; set; }

        public PhysicalDeviceInfo FindByIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity) || Devices == null)
                return null;

            foreach (var device in Devices)
            {
                if (device != null &&
                    string.Equals(
                        device.Identity,
                        identity.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }
            return null;
        }

        public PhysicalDeviceInfo FindByTransportSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial) || Devices == null)
                return null;

            foreach (var device in Devices)
            {
                if (device != null && device.FindTransport(serial) != null)
                    return device;
            }
            return null;
        }

        public DeviceRegistrySnapshot Clone()
        {
            var copy = new DeviceRegistrySnapshot
            {
                Generation = Generation,
                CapturedAtUtc = CapturedAtUtc
            };
            if (Devices != null)
            {
                foreach (var device in Devices)
                {
                    if (device != null) copy.Devices.Add(device.Clone());
                }
            }
            return copy;
        }
    }

    public sealed class DeviceRegistrySnapshotChangedEventArgs : EventArgs
    {
        public DeviceRegistrySnapshotChangedEventArgs(
            DeviceRegistrySnapshot previous,
            DeviceRegistrySnapshot current)
        {
            Previous = previous;
            Current = current;
        }

        public DeviceRegistrySnapshot Previous { get; private set; }
        public DeviceRegistrySnapshot Current { get; private set; }
    }
}
