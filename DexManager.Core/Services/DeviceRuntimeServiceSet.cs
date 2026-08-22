using System;

namespace DexManager.Services
{
    public sealed class DeviceRuntimeServiceSet
    {
        internal DeviceRuntimeServiceSet(
            FileTransferCoordinator fileTransfers,
            PhoneTransferReceiver phoneTransfers,
            CompanionGuardianService companionGuardian,
            ScrcpyService scrcpy,
            SingleWindowService singleWindows,
            ScreenOffService screenOff,
            VirtualDisplayService virtualDisplay,
            DexOrchestrator dex)
        {
            InstanceId = Guid.NewGuid();
            FileTransfers = fileTransfers ??
                throw new ArgumentNullException("fileTransfers");
            PhoneTransfers = phoneTransfers ??
                throw new ArgumentNullException("phoneTransfers");
            CompanionGuardian = companionGuardian ??
                throw new ArgumentNullException("companionGuardian");
            Scrcpy = scrcpy ?? throw new ArgumentNullException("scrcpy");
            SingleWindows = singleWindows ??
                throw new ArgumentNullException("singleWindows");
            ScreenOff = screenOff ??
                throw new ArgumentNullException("screenOff");
            VirtualDisplay = virtualDisplay ??
                throw new ArgumentNullException("virtualDisplay");
            Dex = dex ?? throw new ArgumentNullException("dex");
        }

        public Guid InstanceId { get; private set; }
        public FileTransferCoordinator FileTransfers { get; private set; }
        public PhoneTransferReceiver PhoneTransfers { get; private set; }
        public CompanionGuardianService CompanionGuardian { get; private set; }
        public ScrcpyService Scrcpy { get; private set; }
        public SingleWindowService SingleWindows { get; private set; }
        public ScreenOffService ScreenOff { get; private set; }
        public VirtualDisplayService VirtualDisplay { get; private set; }
        public DexOrchestrator Dex { get; private set; }
    }
}
