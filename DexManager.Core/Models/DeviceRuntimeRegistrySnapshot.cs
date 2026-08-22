using System;
using System.Collections.Generic;

namespace DexManager.Models
{
    public sealed class DeviceRuntimeRegistrySnapshot
    {
        public DeviceRuntimeRegistrySnapshot()
        {
            Sessions = new List<DeviceRuntimeSessionSnapshot>();
        }

        public long Generation { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public IList<DeviceRuntimeSessionSnapshot> Sessions { get; set; }

        public DeviceRuntimeSessionSnapshot FindByIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return null;
            foreach (var session in Sessions)
            {
                if (session != null && string.Equals(
                        session.Identity,
                        identity.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }
            return null;
        }

        public DeviceRuntimeSessionSnapshot FindByTransportSerial(
            string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return null;
            foreach (var session in Sessions)
            {
                if (session == null || session.TransportSerials == null)
                    continue;
                foreach (var candidate in session.TransportSerials)
                {
                    if (string.Equals(
                            candidate,
                            serial.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return session;
                    }
                }
            }
            return null;
        }
    }

    public sealed class DeviceRuntimeRegistryChangedEventArgs : EventArgs
    {
        public DeviceRuntimeRegistryChangedEventArgs(
            DeviceRuntimeRegistrySnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public DeviceRuntimeRegistrySnapshot Snapshot { get; private set; }
    }
}
