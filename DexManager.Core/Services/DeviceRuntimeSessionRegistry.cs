using System;
using System.Collections.Generic;
using DexManager.Models;

namespace DexManager.Services
{
    public sealed class DeviceRuntimeSessionRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, RuntimeSessionState> _sessions =
            new Dictionary<string, RuntimeSessionState>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _identityByTransport =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        private long _generation;

        public event EventHandler<DeviceRuntimeRegistryChangedEventArgs>
            Changed;

        public DeviceRuntimeRegistrySnapshot Current
        {
            get
            {
                lock (_sync) return CreateSnapshotLocked();
            }
        }

        public void Reconcile(DeviceRegistrySnapshot devices)
        {
            var now = DateTime.UtcNow;
            DeviceRuntimeRegistrySnapshot snapshot = null;
            var changed = false;
            lock (_sync)
            {
                var connectedIdentities = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var device in devices == null ||
                        devices.Devices == null
                        ? new List<PhysicalDeviceInfo>()
                        : devices.Devices)
                {
                    if (device == null ||
                        string.IsNullOrWhiteSpace(device.Identity)) continue;

                    var identity = device.Identity.Trim();
                    RuntimeSessionState state;
                    if (!_sessions.TryGetValue(identity, out state))
                    {
                        state = MigrateTemporarySessionLocked(device) ??
                            new RuntimeSessionState(identity);
                        _sessions[identity] = state;
                        changed = true;
                    }

                    connectedIdentities.Add(identity);
                    var displayName = device.DisplayName ?? string.Empty;
                    if (!string.Equals(
                            state.Identity,
                            identity,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(
                            state.DisplayName,
                            displayName,
                            StringComparison.CurrentCulture) ||
                        state.IsConnected != device.IsConnected)
                    {
                        state.Identity = identity;
                        state.DisplayName = displayName;
                        state.IsConnected = device.IsConnected;
                        state.Revision++;
                        changed = true;
                    }
                    state.LastSeenUtc = now;
                    var transports = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    if (device.Transports != null)
                    {
                        foreach (var transport in device.Transports)
                        {
                            if (transport == null ||
                                string.IsNullOrWhiteSpace(transport.Serial))
                            {
                                continue;
                            }
                            var serial = transport.Serial.Trim();
                            transports.Add(serial);
                            _identityByTransport[serial] = identity;
                        }
                    }

                    if (!state.TransportSerials.SetEquals(transports))
                    {
                        state.TransportSerials.Clear();
                        foreach (var serial in transports)
                            state.TransportSerials.Add(serial);
                        state.Revision++;
                        changed = true;
                    }

                    var preferred = device.SelectPreferredTransport(
                        state.ActiveTransportSerial);
                    var activeTransportSerial = preferred == null
                        ? string.Empty
                        : preferred.Serial;
                    if (!string.Equals(
                            state.ActiveTransportSerial,
                            activeTransportSerial,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        state.ActiveTransportSerial = activeTransportSerial;
                        state.Revision++;
                        changed = true;
                    }
                }

                foreach (var item in _sessions)
                {
                    if (connectedIdentities.Contains(item.Key)) continue;
                    if (item.Value.IsConnected)
                    {
                        item.Value.IsConnected = false;
                        item.Value.Revision++;
                        changed = true;
                    }
                }

                if (changed)
                {
                    _generation++;
                    snapshot = CreateSnapshotLocked();
                }
            }
            if (snapshot != null) RaiseChanged(snapshot);
        }

        public void BindServiceInstance(string serial, Guid instanceId)
        {
            if (string.IsNullOrWhiteSpace(serial))
                throw new ArgumentException(
                    "A device transport serial is required.",
                    "serial");
            if (instanceId == Guid.Empty)
                throw new ArgumentException(
                    "A runtime service instance ID is required.",
                    "instanceId");
            DeviceRuntimeRegistrySnapshot snapshot = null;
            lock (_sync)
            {
                var state = GetOrCreateForSerialLocked(serial);
                if (state.ServiceInstanceId != Guid.Empty &&
                    state.ServiceInstanceId != instanceId)
                {
                    throw new InvalidOperationException(
                        "A physical device runtime cannot be rebound to " +
                        "another service instance.");
                }
                if (state.ServiceInstanceId == instanceId) return;
                state.ServiceInstanceId = instanceId;
                state.Revision++;
                _generation++;
                snapshot = CreateSnapshotLocked();
            }
            RaiseChanged(snapshot);
        }

        public string ResolveIdentity(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return string.Empty;
            lock (_sync)
            {
                string identity;
                return _identityByTransport.TryGetValue(
                    serial.Trim(),
                    out identity)
                    ? identity
                    : PhysicalDeviceRegistry.CreateTemporaryIdentity(serial);
            }
        }

        public void SetDexSession(string serial, ManagedDisplaySession session)
        {
            UpdateForSerial(serial, delegate(RuntimeSessionState state)
            {
                state.Dex = session == null
                    ? new DexRuntimeSnapshot()
                    : new DexRuntimeSnapshot
                    {
                        IsRunning = true,
                        Serial = session.Serial ?? string.Empty,
                        DisplayId = session.DisplayId,
                        ProcessId = session.ScrcpyProcessId,
                        OwnsOverlaySetting = session.DisplayLease != null &&
                            session.DisplayLease.OwnsOverlaySetting
                    };
            });
        }

        public void SetSingleWindow(
            string serial,
            int slot,
            int displayId,
            int processId,
            IntPtr windowHandle,
            bool stayAwakeRequested,
            bool screenOffRequested)
        {
            if (slot < 1) throw new ArgumentOutOfRangeException("slot");
            UpdateForSerial(serial, delegate(RuntimeSessionState state)
            {
                state.SingleWindows[slot] = new SingleWindowRuntimeSnapshot
                {
                    Slot = slot,
                    IsRunning = true,
                    Serial = serial.Trim(),
                    DisplayId = displayId,
                    ProcessId = processId,
                    WindowHandle = windowHandle,
                    StayAwakeRequested = stayAwakeRequested,
                    ScreenOffRequested = screenOffRequested
                };
            });
        }

        public void ClearSingleWindow(string serial, int slot)
        {
            UpdateForSerial(serial, delegate(RuntimeSessionState state)
            {
                state.SingleWindows.Remove(slot);
            });
        }

        public void SetCompanionAttached(string serial, bool attached)
        {
            UpdateForSerial(serial, delegate(RuntimeSessionState state)
            {
                state.Companion.IsAttached = attached;
                state.Companion.Serial = attached
                    ? serial.Trim()
                    : string.Empty;
                state.Companion.Generation++;
            });
        }

        public void SetPcToPhoneTransferState(
            string serial,
            int activeSessions,
            int queuedItems)
        {
            UpdateForSerial(serial, delegate(RuntimeSessionState state)
            {
                state.Transfers.ActivePcToPhoneSessions = Math.Max(
                    activeSessions,
                    0);
                state.Transfers.QueuedPcToPhoneItems = Math.Max(
                    queuedItems,
                    0);
            });
        }

        public void SetPhoneToPcActiveTransfers(string serial, int count)
        {
            UpdateForSerial(serial, delegate(RuntimeSessionState state)
            {
                state.Transfers.ActivePhoneToPcTransfers = Math.Max(count, 0);
            });
        }

        public void SetPhonePowerState(
            string serial,
            bool screenOffRequested,
            bool stayAwakeOverrideApplied,
            string originalStayAwakeValue,
            bool wakePending)
        {
            UpdateForSerial(serial, delegate(RuntimeSessionState state)
            {
                state.PhonePower.ScreenOffRequested = screenOffRequested;
                state.PhonePower.StayAwakeOverrideApplied =
                    stayAwakeOverrideApplied;
                state.PhonePower.OriginalStayAwakeValue =
                    originalStayAwakeValue ?? string.Empty;
                state.PhonePower.WakePending = wakePending;
            });
        }

        private void UpdateForSerial(
            string serial,
            Action<RuntimeSessionState> update)
        {
            if (string.IsNullOrWhiteSpace(serial))
                throw new ArgumentException(
                    "A device transport serial is required.",
                    "serial");
            DeviceRuntimeRegistrySnapshot snapshot;
            lock (_sync)
            {
                var state = GetOrCreateForSerialLocked(serial);
                update(state);
                state.Revision++;
                _generation++;
                snapshot = CreateSnapshotLocked();
            }
            RaiseChanged(snapshot);
        }

        private RuntimeSessionState GetOrCreateForSerialLocked(string serial)
        {
            var normalized = serial.Trim();
            string identity;
            if (!_identityByTransport.TryGetValue(normalized, out identity))
            {
                identity = PhysicalDeviceRegistry
                    .CreateTemporaryIdentity(normalized);
                _identityByTransport[normalized] = identity;
            }

            RuntimeSessionState state;
            if (!_sessions.TryGetValue(identity, out state))
            {
                state = new RuntimeSessionState(identity);
                state.TransportSerials.Add(normalized);
                state.ActiveTransportSerial = normalized;
                _sessions[identity] = state;
            }
            return state;
        }

        private RuntimeSessionState MigrateTemporarySessionLocked(
            PhysicalDeviceInfo device)
        {
            if (device.Transports == null) return null;
            foreach (var transport in device.Transports)
            {
                if (transport == null ||
                    string.IsNullOrWhiteSpace(transport.Serial)) continue;
                var temporaryIdentity = PhysicalDeviceRegistry
                    .CreateTemporaryIdentity(transport.Serial);
                RuntimeSessionState temporary;
                if (!_sessions.TryGetValue(
                        temporaryIdentity,
                        out temporary)) continue;
                _sessions.Remove(temporaryIdentity);
                return temporary;
            }
            return null;
        }

        private DeviceRuntimeRegistrySnapshot CreateSnapshotLocked()
        {
            var snapshot = new DeviceRuntimeRegistrySnapshot
            {
                Generation = _generation,
                CapturedAtUtc = DateTime.UtcNow
            };
            foreach (var state in _sessions.Values)
                snapshot.Sessions.Add(state.CreateSnapshot());
            return snapshot;
        }

        private void RaiseChanged(DeviceRuntimeRegistrySnapshot snapshot)
        {
            var handler = Changed;
            if (handler != null)
            {
                handler(
                    this,
                    new DeviceRuntimeRegistryChangedEventArgs(snapshot));
            }
        }

        private sealed class RuntimeSessionState
        {
            internal RuntimeSessionState(string identity)
            {
                Identity = identity;
                DisplayName = string.Empty;
                ActiveTransportSerial = string.Empty;
                LastSeenUtc = DateTime.UtcNow;
                TransportSerials = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                Dex = new DexRuntimeSnapshot();
                SingleWindows = new Dictionary<
                    int,
                    SingleWindowRuntimeSnapshot>();
                Companion = new CompanionRuntimeSnapshot();
                Transfers = new TransferRuntimeSnapshot();
                PhonePower = new PhonePowerRuntimeSnapshot();
            }

            internal string Identity;
            internal string DisplayName;
            internal string ActiveTransportSerial;
            internal bool IsConnected;
            internal Guid ServiceInstanceId;
            internal DateTime LastSeenUtc;
            internal long Revision;
            internal HashSet<string> TransportSerials;
            internal DexRuntimeSnapshot Dex;
            internal Dictionary<int, SingleWindowRuntimeSnapshot>
                SingleWindows;
            internal CompanionRuntimeSnapshot Companion;
            internal TransferRuntimeSnapshot Transfers;
            internal PhonePowerRuntimeSnapshot PhonePower;

            internal DeviceRuntimeSessionSnapshot CreateSnapshot()
            {
                var snapshot = new DeviceRuntimeSessionSnapshot
                {
                    Identity = Identity,
                    DisplayName = DisplayName,
                    ActiveTransportSerial = ActiveTransportSerial,
                    IsConnected = IsConnected,
                    ServiceInstanceId = ServiceInstanceId,
                    LastSeenUtc = LastSeenUtc,
                    Revision = Revision,
                    Dex = Copy(Dex),
                    Companion = Copy(Companion),
                    Transfers = Copy(Transfers),
                    PhonePower = Copy(PhonePower)
                };
                foreach (var serial in TransportSerials)
                    snapshot.TransportSerials.Add(serial);
                foreach (var item in SingleWindows)
                    snapshot.SingleWindows.Add(Copy(item.Value));
                return snapshot;
            }

            private static DexRuntimeSnapshot Copy(DexRuntimeSnapshot value)
            {
                return new DexRuntimeSnapshot
                {
                    IsRunning = value.IsRunning,
                    Serial = value.Serial,
                    DisplayId = value.DisplayId,
                    ProcessId = value.ProcessId,
                    OwnsOverlaySetting = value.OwnsOverlaySetting
                };
            }

            private static SingleWindowRuntimeSnapshot Copy(
                SingleWindowRuntimeSnapshot value)
            {
                return new SingleWindowRuntimeSnapshot
                {
                    Slot = value.Slot,
                    IsRunning = value.IsRunning,
                    Serial = value.Serial,
                    DisplayId = value.DisplayId,
                    ProcessId = value.ProcessId,
                    WindowHandle = value.WindowHandle,
                    StayAwakeRequested = value.StayAwakeRequested,
                    ScreenOffRequested = value.ScreenOffRequested
                };
            }

            private static CompanionRuntimeSnapshot Copy(
                CompanionRuntimeSnapshot value)
            {
                return new CompanionRuntimeSnapshot
                {
                    IsAttached = value.IsAttached,
                    Serial = value.Serial,
                    Generation = value.Generation
                };
            }

            private static TransferRuntimeSnapshot Copy(
                TransferRuntimeSnapshot value)
            {
                return new TransferRuntimeSnapshot
                {
                    ActivePcToPhoneSessions =
                        value.ActivePcToPhoneSessions,
                    QueuedPcToPhoneItems = value.QueuedPcToPhoneItems,
                    ActivePhoneToPcTransfers =
                        value.ActivePhoneToPcTransfers
                };
            }

            private static PhonePowerRuntimeSnapshot Copy(
                PhonePowerRuntimeSnapshot value)
            {
                return new PhonePowerRuntimeSnapshot
                {
                    ScreenOffRequested = value.ScreenOffRequested,
                    StayAwakeOverrideApplied =
                        value.StayAwakeOverrideApplied,
                    OriginalStayAwakeValue = value.OriginalStayAwakeValue,
                    WakePending = value.WakePending
                };
            }
        }
    }
}
