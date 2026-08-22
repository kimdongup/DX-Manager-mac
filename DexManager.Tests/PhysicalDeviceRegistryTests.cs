using System;
using System.Linq;
using DexManager.Models;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public class PhysicalDeviceRegistryTests
    {
        [Fact]
        public void MergesUsbAndWirelessForSamePhysicalDevice()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb),
                Device("phone-a", "Galaxy A", "192.168.0.2:5555", DeviceTransportKind.Wireless)
            });

            Assert.Single(snapshot.Devices);
            Assert.Equal(2, snapshot.Devices[0].Transports.Count);
            Assert.Equal("USB-A", snapshot.Devices[0].SelectPreferredTransport(null).Serial);
        }

        [Fact]
        public void HonorsExplicitAuthorizedTransport()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb),
                Device("phone-a", "Galaxy A", "192.168.0.2:5555", DeviceTransportKind.Wireless)
            });

            Assert.Equal(
                "192.168.0.2:5555",
                snapshot.Devices[0].SelectPreferredTransport("192.168.0.2:5555").Serial);
        }

        [Fact]
        public void UsbPolicyDoesNotFallBackToWireless()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "192.168.0.2:5555", DeviceTransportKind.Wireless)
            });

            Assert.Null(snapshot.Devices[0].SelectAuthorizedTransport(DeviceTransportKind.Usb, string.Empty));
        }

        [Fact]
        public void WirelessPolicyDoesNotFallBackToUsb()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });

            Assert.Null(snapshot.Devices[0].SelectAuthorizedTransport(DeviceTransportKind.Wireless, "192.168.0.2:5555"));
        }

        [Fact]
        public void KeepsDifferentPhysicalDevicesSeparate()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy", "USB-A", DeviceTransportKind.Usb),
                Device("phone-b", "Galaxy", "USB-B", DeviceTransportKind.Usb)
            });

            Assert.Equal(2, snapshot.Devices.Count);
            Assert.NotNull(snapshot.FindByIdentity("phone-a"));
            Assert.NotNull(snapshot.FindByIdentity("phone-b"));
        }

        [Fact]
        public void OrdersStartupDevicesByModelGeneration()
        {
            var order = new DevicePresentationOrder();
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-old", "현호의 Galaxy S20 FE 5G", "USB-OLD", DeviceTransportKind.Usb),
                Device("phone-new", "현호의 S26 Ultra", "USB-NEW", DeviceTransportKind.Usb)
            });
            order.Reconcile(snapshot.Devices);

            var identities = order.GetIdentities();
            Assert.Equal(2, identities.Count);
            Assert.Equal("phone-new", identities[0]);
        }

        [Fact]
        public void PreservesSequentialConnectionOrder()
        {
            var order = new DevicePresentationOrder();
            var registry = new PhysicalDeviceRegistry();
            var first = registry.Reconcile(new[]
            {
                Device("phone-old", "현호의 Galaxy S20 FE 5G", "USB-OLD", DeviceTransportKind.Usb)
            });
            order.Reconcile(first.Devices);

            var second = registry.Reconcile(new[]
            {
                Device("phone-new", "현호의 S26 Ultra", "USB-NEW", DeviceTransportKind.Usb),
                Device("phone-old", "현호의 Galaxy S20 FE 5G", "USB-OLD", DeviceTransportKind.Usb)
            });
            order.Reconcile(second.Devices);

            var identities = order.GetIdentities();
            Assert.Equal("phone-old", identities[0]);
            Assert.Equal("phone-new", identities[1]);
        }

        [Fact]
        public void CreatesTemporaryIdentityWhenStableIdentityIsMissing()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device(null, null, "USB-PENDING", DeviceTransportKind.Usb)
            });

            Assert.Equal("transport:USB-PENDING", snapshot.Devices[0].Identity);
            Assert.True(PhysicalDeviceRegistry.IsTemporaryIdentity(snapshot.Devices[0].Identity));
        }

        [Fact]
        public void IgnoresTimestampOnlyRefreshes()
        {
            var registry = new PhysicalDeviceRegistry();
            var eventCount = 0;
            registry.SnapshotChanged += delegate { eventCount++; };

            var first = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });
            var second = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });

            Assert.Equal(1L, first.Generation);
            Assert.Equal(first.Generation, second.Generation);
            Assert.Equal(1, eventCount);
        }

        [Fact]
        public void PublishesMeaningfulStatusChanges()
        {
            var registry = new PhysicalDeviceRegistry();
            var eventCount = 0;
            registry.SnapshotChanged += delegate { eventCount++; };

            registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });

            var changed = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb, AdbDeviceStatus.Offline)
            });

            Assert.Equal(2L, changed.Generation);
            Assert.Equal(2, eventCount);
            Assert.False(changed.Devices[0].IsConnected);
        }

        [Fact]
        public void RemovesMissingTransportsOnReconcile()
        {
            var registry = new PhysicalDeviceRegistry();
            registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb),
                Device("phone-a", "Galaxy A", "192.168.0.2:5555", DeviceTransportKind.Wireless)
            });

            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "192.168.0.2:5555", DeviceTransportKind.Wireless)
            });

            Assert.Single(snapshot.Devices[0].Transports);
            Assert.Equal("192.168.0.2:5555", snapshot.Devices[0].Transports[0].Serial);
        }

        [Fact]
        public void ReturnsDefensiveSnapshots()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });

            snapshot.Devices.Clear();
            Assert.Single(registry.Current.Devices);
        }

        [Fact]
        public void PrefersStableAuthorizedDuplicateObservation()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device(null, null, "USB-A", DeviceTransportKind.Usb, AdbDeviceStatus.Offline),
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });

            Assert.Single(snapshot.Devices);
            Assert.Equal("phone-a", snapshot.Devices[0].Identity);
            Assert.True(snapshot.Devices[0].IsConnected);
        }

        [Fact]
        public void PreservesKnownDisplayName()
        {
            var registry = new PhysicalDeviceRegistry();
            registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });

            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", null, "USB-A", DeviceTransportKind.Usb)
            });

            Assert.Equal("Galaxy A", snapshot.Devices[0].DisplayName);
        }

        [Fact]
        public void PreservesKnownIdentityWhenTransportCannotBeQueried()
        {
            var registry = new PhysicalDeviceRegistry();
            registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "192.168.0.2:5555", DeviceTransportKind.Wireless)
            });

            var snapshot = registry.Reconcile(new[]
            {
                Device(null, null, "192.168.0.2:5555", DeviceTransportKind.Wireless, AdbDeviceStatus.Offline)
            });

            Assert.Equal("phone-a", snapshot.Devices[0].Identity);
            Assert.Equal("Galaxy A", snapshot.Devices[0].DisplayName);
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
