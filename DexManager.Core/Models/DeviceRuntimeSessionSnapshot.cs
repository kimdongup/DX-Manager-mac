using System;
using System.Collections.Generic;

namespace DexManager.Models
{
    public sealed class DeviceRuntimeSessionSnapshot
    {
        public DeviceRuntimeSessionSnapshot()
        {
            TransportSerials = new List<string>();
            SingleWindows = new List<SingleWindowRuntimeSnapshot>();
            Dex = new DexRuntimeSnapshot();
            Companion = new CompanionRuntimeSnapshot();
            Transfers = new TransferRuntimeSnapshot();
            PhonePower = new PhonePowerRuntimeSnapshot();
        }

        public string Identity { get; set; }
        public string DisplayName { get; set; }
        public string ActiveTransportSerial { get; set; }
        public bool IsConnected { get; set; }
        public Guid ServiceInstanceId { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public long Revision { get; set; }
        public IList<string> TransportSerials { get; set; }
        public DexRuntimeSnapshot Dex { get; set; }
        public IList<SingleWindowRuntimeSnapshot> SingleWindows { get; set; }
        public CompanionRuntimeSnapshot Companion { get; set; }
        public TransferRuntimeSnapshot Transfers { get; set; }
        public PhonePowerRuntimeSnapshot PhonePower { get; set; }
    }

    public sealed class DexRuntimeSnapshot
    {
        public bool IsRunning { get; set; }
        public string Serial { get; set; }
        public int DisplayId { get; set; }
        public int ProcessId { get; set; }
        public bool OwnsOverlaySetting { get; set; }
    }

    public sealed class SingleWindowRuntimeSnapshot
    {
        public int Slot { get; set; }
        public bool IsRunning { get; set; }
        public string Serial { get; set; }
        public int DisplayId { get; set; }
        public int ProcessId { get; set; }
        public IntPtr WindowHandle { get; set; }
        public bool StayAwakeRequested { get; set; }
        public bool ScreenOffRequested { get; set; }
    }

    public sealed class CompanionRuntimeSnapshot
    {
        public bool IsAttached { get; set; }
        public string Serial { get; set; }
        public long Generation { get; set; }
    }

    public sealed class TransferRuntimeSnapshot
    {
        public int ActivePcToPhoneSessions { get; set; }
        public int QueuedPcToPhoneItems { get; set; }
        public int ActivePhoneToPcTransfers { get; set; }
    }

    public sealed class PhonePowerRuntimeSnapshot
    {
        public bool ScreenOffRequested { get; set; }
        public bool StayAwakeOverrideApplied { get; set; }
        public string OriginalStayAwakeValue { get; set; }
        public bool WakePending { get; set; }
    }
}
