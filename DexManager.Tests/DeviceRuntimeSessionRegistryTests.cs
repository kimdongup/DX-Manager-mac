using System;
using DexManager.Models;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public class DeviceRuntimeSessionRegistryTests
    {
        [Fact]
        public void CreatesIndependentRuntimeSessions()
        {
            var registry = CreateRuntimeRegistry();
            var snapshot = registry.Current;

            Assert.Equal(2, snapshot.Sessions.Count);
            Assert.NotNull(snapshot.FindByIdentity("phone-a"));
            Assert.NotNull(snapshot.FindByIdentity("phone-b"));
        }

        [Fact]
        public void KeepsRuntimeUpdatesScopedToOnePhysicalDevice()
        {
            var registry = CreateRuntimeRegistry();
            registry.SetDexSession("USB-A", new ManagedDisplaySession
            {
                Serial = "USB-A",
                DisplayId = 12,
                ScrcpyProcessId = 101,
                DisplayLease = new VirtualDisplayLease
                {
                    Serial = "USB-A",
                    DisplayId = 12,
                    OwnsOverlaySetting = true
                }
            });
            registry.SetSingleWindow("USB-B", 1, 21, 202, new IntPtr(303), true, false);
            registry.SetCompanionAttached("USB-B", true);
            registry.SetPcToPhoneTransferState("USB-B", 1, 4);
            registry.SetPhonePowerState("USB-A", true, true, "0", true);

            var snapshot = registry.Current;
            var first = snapshot.FindByIdentity("phone-a");
            var second = snapshot.FindByIdentity("phone-b");

            Assert.True(first.Dex.IsRunning);
            Assert.Empty(first.SingleWindows);
            Assert.True(first.PhonePower.ScreenOffRequested);

            Assert.False(second.Dex.IsRunning);
            Assert.Single(second.SingleWindows);
            Assert.True(second.Companion.IsAttached);
            Assert.Equal(4, second.Transfers.QueuedPcToPhoneItems);
        }

        [Fact]
        public void SharesRuntimeAcrossUsbAndWirelessTransports()
        {
            var physical = new PhysicalDeviceRegistry();
            var runtime = new DeviceRuntimeSessionRegistry();
            runtime.Reconcile(physical.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb),
                Device("phone-a", "Galaxy A", "10.0.0.2:5555", DeviceTransportKind.Wireless)
            }));
            runtime.SetCompanionAttached("10.0.0.2:5555", true);

            var snapshot = runtime.Current;
            Assert.Single(snapshot.Sessions);
            Assert.True(snapshot.FindByTransportSerial("USB-A").Companion.IsAttached);
        }

        [Fact]
        public void MigratesTemporaryRuntimeIdentity()
        {
            var runtime = new DeviceRuntimeSessionRegistry();
            runtime.SetPhonePowerState("USB-A", true, false, string.Empty, true);

            var physical = new PhysicalDeviceRegistry();
            runtime.Reconcile(physical.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            }));

            var snapshot = runtime.Current;
            Assert.Single(snapshot.Sessions);
            var migrated = snapshot.FindByIdentity("phone-a");
            Assert.NotNull(migrated);
            Assert.True(migrated.PhonePower.ScreenOffRequested);
        }

        [Fact]
        public void PreservesRuntimeStateAcrossDisconnect()
        {
            var physical = new PhysicalDeviceRegistry();
            var runtime = new DeviceRuntimeSessionRegistry();
            runtime.Reconcile(physical.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            }));
            runtime.SetPcToPhoneTransferState("USB-A", 1, 2);
            runtime.Reconcile(physical.Reset());

            var session = runtime.Current.FindByIdentity("phone-a");
            Assert.NotNull(session);
            Assert.False(session.IsConnected);
            Assert.Equal(2, session.Transfers.QueuedPcToPhoneItems);
        }

        [Fact]
        public void IgnoresUnchangedRuntimeReconciles()
        {
            var physical = new PhysicalDeviceRegistry();
            var runtime = new DeviceRuntimeSessionRegistry();
            var devices = new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            };
            runtime.Reconcile(physical.Reconcile(devices));
            var before = runtime.Current;
            var eventCount = 0;
            runtime.Changed += delegate { eventCount++; };

            runtime.Reconcile(physical.Reconcile(devices));

            var after = runtime.Current;
            Assert.Equal(0, eventCount);
            Assert.Equal(before.Generation, after.Generation);
            Assert.Equal(before.Sessions[0].Revision, after.Sessions[0].Revision);
        }

        [Fact]
        public void BindsOneServiceInstancePerPhysicalDevice()
        {
            var registry = CreateRuntimeRegistry();
            var firstServices = Guid.NewGuid();
            var secondServices = Guid.NewGuid();
            var eventCount = 0;
            registry.Changed += delegate { eventCount++; };

            registry.BindServiceInstance("USB-A", firstServices);
            registry.BindServiceInstance("USB-A", firstServices);
            registry.BindServiceInstance("USB-B", secondServices);

            var snapshot = registry.Current;
            Assert.Equal(firstServices, snapshot.FindByIdentity("phone-a").ServiceInstanceId);
            Assert.Equal(secondServices, snapshot.FindByIdentity("phone-b").ServiceInstanceId);
            Assert.Equal(2, eventCount);

            Assert.Throws<InvalidOperationException>(() =>
            {
                registry.BindServiceInstance("USB-A", secondServices);
            });
        }

        [Fact]
        public void KeepsBoundRuntimeWhenPreferredTransportChanges()
        {
            var physical = new PhysicalDeviceRegistry();
            var runtime = new DeviceRuntimeSessionRegistry();
            runtime.Reconcile(physical.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb),
                Device("phone-a", "Galaxy A", "10.0.0.2:5555", DeviceTransportKind.Wireless),
                Device("phone-b", "Galaxy B", "USB-B", DeviceTransportKind.Usb)
            }));
            var phoneAService = Guid.NewGuid();
            var phoneBService = Guid.NewGuid();
            runtime.BindServiceInstance("USB-A", phoneAService);
            runtime.BindServiceInstance("USB-B", phoneBService);
            runtime.SetDexSession("USB-A", new ManagedDisplaySession
            {
                Serial = "USB-A",
                DisplayId = 21,
                ScrcpyProcessId = 101
            });

            runtime.Reconcile(physical.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "10.0.0.2:5555", DeviceTransportKind.Wireless),
                Device("phone-b", "Galaxy B", "USB-B", DeviceTransportKind.Usb)
            }));

            var snapshot = runtime.Current;
            var phoneA = snapshot.FindByIdentity("phone-a");
            var phoneB = snapshot.FindByIdentity("phone-b");

            Assert.Equal(phoneAService, phoneA.ServiceInstanceId);
            Assert.Equal("10.0.0.2:5555", phoneA.ActiveTransportSerial);
            Assert.True(phoneA.Dex.IsRunning);
            Assert.Equal(phoneBService, phoneB.ServiceInstanceId);
            Assert.False(phoneB.Dex.IsRunning);
        }

        private static DeviceRuntimeSessionRegistry CreateRuntimeRegistry()
        {
            var physical = new PhysicalDeviceRegistry();
            var runtime = new DeviceRuntimeSessionRegistry();
            runtime.Reconcile(physical.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb),
                Device("phone-b", "Galaxy B", "USB-B", DeviceTransportKind.Usb)
            }));
            return runtime;
        }

        private static DiscoveredDeviceTransport Device(
            string identity,
            string name,
            string serial,
            DeviceTransportKind kind,
            AdbDeviceStatus status = AdbDeviceStatus.Device)
        {
            return new DiscoveredDeviceTransport
            {
                DeviceIdentity = identity,
                DisplayName = name,
                Serial = serial,
                Kind = kind,
                Status = status,
                RawStatus = status.ToString().ToLowerInvariant()
            };
        }
    }
}
