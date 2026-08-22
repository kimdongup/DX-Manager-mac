using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using DexManager.Models;
using DexManager.Services;
using DexManager.Utils;

namespace DexManager.MultiDeviceTests
{
    internal static class Program
    {
        private static int _passed;

        private static int Main()
        {
            var tests = new Action[]
            {
                MergesUsbAndWirelessForSamePhysicalDevice,
                HonorsExplicitAuthorizedTransport,
                UsbPolicyDoesNotFallBackToWireless,
                WirelessPolicyDoesNotFallBackToUsb,
                KeepsDifferentPhysicalDevicesSeparate,
                OrdersStartupDevicesByModelGeneration,
                PreservesSequentialConnectionOrder,
                CreatesTemporaryIdentityWhenStableIdentityIsMissing,
                IgnoresTimestampOnlyRefreshes,
                PublishesMeaningfulStatusChanges,
                RemovesMissingTransportsOnReconcile,
                ReturnsDefensiveSnapshots,
                PrefersStableAuthorizedDuplicateObservation,
                PreservesKnownDisplayName,
                PreservesKnownIdentityWhenTransportCannotBeQueried,
                RequiresExplicitSerialForDeviceCommands,
                KeepsCleanupCommandsScopedToRequestedDevice,
                CombinesWindowsShutdownCleanupIntoOneDeviceCommand,
                InterleavedDeviceCommandsDoNotShareTarget,
                DeviceCancellationMatchesOnlyRequestedSerial,
                CreatesIndependentRuntimeSessions,
                KeepsRuntimeUpdatesScopedToOnePhysicalDevice,
                SharesRuntimeAcrossUsbAndWirelessTransports,
                MigratesTemporaryRuntimeIdentity,
                PreservesRuntimeStateAcrossDisconnect,
                IgnoresUnchangedRuntimeReconciles,
                BindsOneServiceInstancePerPhysicalDevice,
                KeepsBoundRuntimeWhenPreferredTransportChanges,
                KeepsRunSettingsIndependentPerPhysicalDevice,
                SeedsNewDeviceSettingsFromLegacyTemplate,
                PersistsDeviceRunSettingsProfiles,
                KeepsWirelessSettingsIndependentPerPhysicalDevice,
                SeedsSelectedWirelessDeviceFromLegacyConnection,
                PersistsDeviceWirelessConnectionProfiles,
                RoundTripsCompanionGuardianProtocolPayload,
                BlocksNewProcessesAfterShutdown,
                TerminatesActiveProcessOnShutdown,
                TerminatesOnlyConfiguredBundledExecutablePath,
                SerializesConcurrentSettingsSaves
            };

            try
            {
                foreach (var test in tests)
                {
                    test();
                    _passed++;
                    Console.WriteLine("PASS " + test.Method.Name);
                }

                Console.WriteLine(
                    "All multi-device foundation tests passed: " + _passed);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "FAIL after " + _passed + " tests: " + ex.Message);
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void MergesUsbAndWirelessForSamePhysicalDevice()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb),
                Device("phone-a", "Galaxy A", "192.168.0.2:5555", DeviceTransportKind.Wireless)
            });

            Equal(1, snapshot.Devices.Count, "same identity must merge");
            Equal(2, snapshot.Devices[0].Transports.Count, "both transports must remain");
            Equal(
                "USB-A",
                snapshot.Devices[0].SelectPreferredTransport(null).Serial,
                "USB must be the default transport");
        }

        private static void HonorsExplicitAuthorizedTransport()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb),
                Device("phone-a", "Galaxy A", "192.168.0.2:5555", DeviceTransportKind.Wireless)
            });

            Equal(
                "192.168.0.2:5555",
                snapshot.Devices[0]
                    .SelectPreferredTransport("192.168.0.2:5555").Serial,
                "explicit authorized transport must win");
        }

        private static void UsbPolicyDoesNotFallBackToWireless()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device(
                    "phone-a",
                    "Galaxy A",
                    "192.168.0.2:5555",
                    DeviceTransportKind.Wireless)
            });

            True(
                snapshot.Devices[0].SelectAuthorizedTransport(
                    DeviceTransportKind.Usb,
                    string.Empty) == null,
                "USB policy must wait for USB instead of using wireless");
        }

        private static void WirelessPolicyDoesNotFallBackToUsb()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device(
                    "phone-a",
                    "Galaxy A",
                    "USB-A",
                    DeviceTransportKind.Usb)
            });

            True(
                snapshot.Devices[0].SelectAuthorizedTransport(
                    DeviceTransportKind.Wireless,
                    "192.168.0.2:5555") == null,
                "wireless policy must wait instead of using USB");
        }

        private static void KeepsDifferentPhysicalDevicesSeparate()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy", "USB-A", DeviceTransportKind.Usb),
                Device("phone-b", "Galaxy", "USB-B", DeviceTransportKind.Usb)
            });

            Equal(2, snapshot.Devices.Count, "display name must not merge devices");
            NotNull(snapshot.FindByIdentity("phone-a"), "phone-a missing");
            NotNull(snapshot.FindByIdentity("phone-b"), "phone-b missing");
        }

        private static void OrdersStartupDevicesByModelGeneration()
        {
            var order = new DevicePresentationOrder();
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device(
                    "phone-old",
                    "현호의 Galaxy S20 FE 5G",
                    "USB-OLD",
                    DeviceTransportKind.Usb),
                Device(
                    "phone-new",
                    "현호의 S26 Ultra",
                    "USB-NEW",
                    DeviceTransportKind.Usb)
            });
            order.Reconcile(snapshot.Devices);

            var identities = order.GetIdentities();
            Equal(2, identities.Count, "both startup devices must be ordered");
            Equal(
                "phone-new",
                identities[0],
                "newer Galaxy generation must be shown first at startup");
        }

        private static void PreservesSequentialConnectionOrder()
        {
            var order = new DevicePresentationOrder();
            var registry = new PhysicalDeviceRegistry();
            var first = registry.Reconcile(new[]
            {
                Device(
                    "phone-old",
                    "현호의 Galaxy S20 FE 5G",
                    "USB-OLD",
                    DeviceTransportKind.Usb)
            });
            order.Reconcile(first.Devices);
            var second = registry.Reconcile(new[]
            {
                Device(
                    "phone-new",
                    "현호의 S26 Ultra",
                    "USB-NEW",
                    DeviceTransportKind.Usb),
                Device(
                    "phone-old",
                    "현호의 Galaxy S20 FE 5G",
                    "USB-OLD",
                    DeviceTransportKind.Usb)
            });
            order.Reconcile(second.Devices);

            var identities = order.GetIdentities();
            Equal(
                "phone-old",
                identities[0],
                "first connected phone must keep the first position");
            Equal(
                "phone-new",
                identities[1],
                "later connection must be appended");
        }

        private static void CreatesTemporaryIdentityWhenStableIdentityIsMissing()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device(null, null, "USB-PENDING", DeviceTransportKind.Usb)
            });

            Equal(
                "transport:USB-PENDING",
                snapshot.Devices[0].Identity,
                "temporary identity must be transport-scoped");
            True(
                PhysicalDeviceRegistry.IsTemporaryIdentity(
                    snapshot.Devices[0].Identity),
                "temporary identity must be recognizable");
        }

        private static void IgnoresTimestampOnlyRefreshes()
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

            Equal(1L, first.Generation, "first discovery must increment generation");
            Equal(first.Generation, second.Generation, "refresh must not increment generation");
            Equal(1, eventCount, "refresh must not publish a duplicate event");
        }

        private static void PublishesMeaningfulStatusChanges()
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
                Device(
                    "phone-a",
                    "Galaxy A",
                    "USB-A",
                    DeviceTransportKind.Usb,
                    AdbDeviceStatus.Offline)
            });

            Equal(2L, changed.Generation, "status change must increment generation");
            Equal(2, eventCount, "status change must publish an event");
            True(!changed.Devices[0].IsConnected, "offline device must not be connected");
        }

        private static void RemovesMissingTransportsOnReconcile()
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

            Equal(1, snapshot.Devices[0].Transports.Count, "missing transport must be removed");
            Equal(
                "192.168.0.2:5555",
                snapshot.Devices[0].Transports[0].Serial,
                "remaining transport is wrong");
        }

        private static void ReturnsDefensiveSnapshots()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });
            snapshot.Devices.Clear();

            Equal(1, registry.Current.Devices.Count, "caller must not mutate registry state");
        }

        private static void PrefersStableAuthorizedDuplicateObservation()
        {
            var registry = new PhysicalDeviceRegistry();
            var snapshot = registry.Reconcile(new[]
            {
                Device(null, null, "USB-A", DeviceTransportKind.Usb, AdbDeviceStatus.Offline),
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            });

            Equal(1, snapshot.Devices.Count, "duplicate serial must collapse");
            Equal("phone-a", snapshot.Devices[0].Identity, "stable identity must win");
            True(snapshot.Devices[0].IsConnected, "authorized observation must win");
        }

        private static void PreservesKnownDisplayName()
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

            Equal("Galaxy A", snapshot.Devices[0].DisplayName, "known name must be cached");
        }

        private static void PreservesKnownIdentityWhenTransportCannotBeQueried()
        {
            var registry = new PhysicalDeviceRegistry();
            registry.Reconcile(new[]
            {
                Device(
                    "phone-a",
                    "Galaxy A",
                    "192.168.0.2:5555",
                    DeviceTransportKind.Wireless)
            });
            var snapshot = registry.Reconcile(new[]
            {
                Device(
                    null,
                    null,
                    "192.168.0.2:5555",
                    DeviceTransportKind.Wireless,
                    AdbDeviceStatus.Offline)
            });

            Equal(
                "phone-a",
                snapshot.Devices[0].Identity,
                "known transport must remain attached to its physical device");
            Equal(
                "Galaxy A",
                snapshot.Devices[0].DisplayName,
                "known device name must remain available while offline");
        }

        private static void RequiresExplicitSerialForDeviceCommands()
        {
            Throws<ArgumentException>(delegate
            {
                AdbCommandBuilder.ForDevice(
                    string.Empty,
                    "shell get-state");
            }, "device command without a serial must be rejected");
        }

        private static void KeepsCleanupCommandsScopedToRequestedDevice()
        {
            var first = AdbCommandBuilder.ForDevice(
                "PHONE-A",
                "shell settings delete global overlay_display_devices");
            var second = AdbCommandBuilder.ForDevice(
                "PHONE-B",
                "shell settings delete global overlay_display_devices");

            True(first.StartsWith("-s \"PHONE-A\" "),
                "first cleanup must target PHONE-A");
            True(first.IndexOf("PHONE-B", StringComparison.Ordinal) < 0,
                "first cleanup must not contain PHONE-B");
            True(second.StartsWith("-s \"PHONE-B\" "),
                "second cleanup must target PHONE-B");
            True(second.IndexOf("PHONE-A", StringComparison.Ordinal) < 0,
                "second cleanup must not contain PHONE-A");
        }

        private static void CombinesWindowsShutdownCleanupIntoOneDeviceCommand()
        {
            var command = AdbCommandBuilder.ForShellCommands(
                "PHONE-A",
                "settings delete global overlay_display_devices",
                "settings put global stay_on_while_plugged_in 3");

            True(command.StartsWith("-s \"PHONE-A\" shell \""),
                "shutdown cleanup must remain scoped to PHONE-A");
            True(command.IndexOf(
                    "overlay_display_devices; settings put global " +
                    "stay_on_while_plugged_in 3",
                    StringComparison.Ordinal) >= 0,
                "shutdown cleanup must use one combined shell command");
            True(command.IndexOf("PHONE-B", StringComparison.Ordinal) < 0,
                "shutdown cleanup must not leak another target");
        }

        private static void InterleavedDeviceCommandsDoNotShareTarget()
        {
            for (var index = 0; index < 1000; index++)
            {
                var serial = index % 2 == 0 ? "PHONE-A" : "PHONE-B";
                var other = index % 2 == 0 ? "PHONE-B" : "PHONE-A";
                var command = AdbCommandBuilder.ForDevice(
                    serial,
                    "shell echo " + index);
                True(command.StartsWith("-s \"" + serial + "\" "),
                    "interleaved command changed its requested target");
                True(command.IndexOf(other, StringComparison.Ordinal) < 0,
                    "interleaved command leaked another target");
            }
        }

        private static void DeviceCancellationMatchesOnlyRequestedSerial()
        {
            True(
                DeviceSerialScope.Matches("PHONE-A", "phone-a"),
                "requested device cancellation must match its own serial");
            True(
                !DeviceSerialScope.Matches("PHONE-A", "PHONE-B"),
                "requested device cancellation must not match another serial");
            True(
                !DeviceSerialScope.Matches(string.Empty, "PHONE-A"),
                "empty cancellation scope must not match a device");
        }

        private static void CreatesIndependentRuntimeSessions()
        {
            var registry = CreateRuntimeRegistry();
            var snapshot = registry.Current;
            Equal(2, snapshot.Sessions.Count,
                "each physical device needs its own runtime session");
            NotNull(snapshot.FindByIdentity("phone-a"),
                "phone-a runtime missing");
            NotNull(snapshot.FindByIdentity("phone-b"),
                "phone-b runtime missing");
        }

        private static void KeepsRuntimeUpdatesScopedToOnePhysicalDevice()
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
            registry.SetSingleWindow(
                "USB-B", 1, 21, 202, new IntPtr(303), true, false);
            registry.SetCompanionAttached("USB-B", true);
            registry.SetPcToPhoneTransferState("USB-B", 1, 4);
            registry.SetPhonePowerState(
                "USB-A", true, true, "0", true);

            var snapshot = registry.Current;
            var first = snapshot.FindByIdentity("phone-a");
            var second = snapshot.FindByIdentity("phone-b");
            True(first.Dex.IsRunning, "phone-a DeX state missing");
            Equal(0, first.SingleWindows.Count,
                "phone-b single window leaked into phone-a");
            True(first.PhonePower.ScreenOffRequested,
                "phone-a power state missing");
            True(!second.Dex.IsRunning,
                "phone-a DeX state leaked into phone-b");
            Equal(1, second.SingleWindows.Count,
                "phone-b single window state missing");
            True(second.Companion.IsAttached,
                "phone-b companion state missing");
            Equal(4, second.Transfers.QueuedPcToPhoneItems,
                "phone-b transfer queue state missing");
        }

        private static void SharesRuntimeAcrossUsbAndWirelessTransports()
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
            Equal(1, snapshot.Sessions.Count,
                "USB and wireless transports must share one runtime");
            True(snapshot.FindByTransportSerial("USB-A")
                    .Companion.IsAttached,
                "wireless update must be visible through USB alias");
        }

        private static void MigratesTemporaryRuntimeIdentity()
        {
            var runtime = new DeviceRuntimeSessionRegistry();
            runtime.SetPhonePowerState(
                "USB-A", true, false, string.Empty, true);
            var physical = new PhysicalDeviceRegistry();
            runtime.Reconcile(physical.Reconcile(new[]
            {
                Device("phone-a", "Galaxy A", "USB-A", DeviceTransportKind.Usb)
            }));

            var snapshot = runtime.Current;
            Equal(1, snapshot.Sessions.Count,
                "temporary runtime must migrate, not duplicate");
            var migrated = snapshot.FindByIdentity("phone-a");
            NotNull(migrated, "stable runtime identity missing");
            True(migrated.PhonePower.ScreenOffRequested,
                "temporary runtime state was lost during migration");
        }

        private static void PreservesRuntimeStateAcrossDisconnect()
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
            NotNull(session, "disconnected runtime must remain for cleanup");
            True(!session.IsConnected,
                "disconnected runtime must be marked offline");
            Equal(2, session.Transfers.QueuedPcToPhoneItems,
                "disconnect must not erase cleanup evidence");
        }

        private static void IgnoresUnchangedRuntimeReconciles()
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
            Equal(0, eventCount,
                "timestamp-only runtime refresh must not publish an event");
            Equal(before.Generation, after.Generation,
                "timestamp-only runtime refresh must not change generation");
            Equal(
                before.Sessions[0].Revision,
                after.Sessions[0].Revision,
                "timestamp-only runtime refresh must not change revision");
        }

        private static void BindsOneServiceInstancePerPhysicalDevice()
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
            Equal(
                firstServices,
                snapshot.FindByIdentity("phone-a").ServiceInstanceId,
                "phone-a service binding missing");
            Equal(
                secondServices,
                snapshot.FindByIdentity("phone-b").ServiceInstanceId,
                "phone-b service binding missing");
            Equal(2, eventCount,
                "rebinding the same service must not publish an event");
            Throws<InvalidOperationException>(delegate
            {
                registry.BindServiceInstance("USB-A", secondServices);
            }, "a physical device must not be rebound to another service set");
        }

        private static void KeepsBoundRuntimeWhenPreferredTransportChanges()
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
            Equal(phoneAService, phoneA.ServiceInstanceId,
                "phone-a tab must retain its runtime after transport change");
            Equal("10.0.0.2:5555", phoneA.ActiveTransportSerial,
                "phone-a tab must select the remaining wireless transport");
            True(phoneA.Dex.IsRunning,
                "phone-a runtime evidence must survive transport change");
            Equal(phoneBService, phoneB.ServiceInstanceId,
                "phone-b runtime binding must remain isolated");
            True(!phoneB.Dex.IsRunning,
                "phone-a session must not leak into phone-b tab");
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

        private static void KeepsRunSettingsIndependentPerPhysicalDevice()
        {
            var settings = AppSettings.CreateDefault();
            var phoneA = settings.GetOrCreateDeviceRunSettings("phone-a");
            var phoneB = settings.GetOrCreateDeviceRunSettings("phone-b");

            phoneA.VirtualDisplay.Width = 1920;
            phoneA.VirtualDisplay.Height = 1080;
            phoneA.VirtualDisplay.Dpi = 240;
            phoneA.SingleWindowSlots[0].Width = 1280;
            phoneA.SingleWindowSlots[0].StartAppPackage = "app.phone.a";
            phoneA.Scrcpy.BitRate = "20M";

            Equal(1600, phoneB.VirtualDisplay.Width,
                "phone-b DeX resolution must not follow phone-a");
            Equal(900, phoneB.VirtualDisplay.Height,
                "phone-b DeX height must remain independent");
            Equal(150, phoneB.VirtualDisplay.Dpi,
                "phone-b DeX DPI must remain independent");
            Equal(1600, phoneB.SingleWindowSlots[0].Width,
                "phone-b single-window resolution must remain independent");
            True(string.IsNullOrEmpty(
                    phoneB.SingleWindowSlots[0].StartAppPackage),
                "phone-b selected app must not follow phone-a");
            Equal("8M", phoneB.Scrcpy.BitRate,
                "phone-b scrcpy bitrate must remain independent");
        }

        private static void SeedsNewDeviceSettingsFromLegacyTemplate()
        {
            var settings = AppSettings.CreateDefault();
            settings.VirtualDisplay.Width = 2560;
            settings.VirtualDisplay.Height = 1440;
            settings.VirtualDisplay.Dpi = 180;
            settings.Scrcpy.BitRate = "30M";
            settings.SingleWindowSlots[1].Width = 1200;
            settings.SingleWindowSlots[1].Height = 800;

            var migrated = settings.GetOrCreateDeviceRunSettings(
                "existing-phone");

            Equal(2560, migrated.VirtualDisplay.Width,
                "first device profile must preserve existing DeX width");
            Equal(1440, migrated.VirtualDisplay.Height,
                "first device profile must preserve existing DeX height");
            Equal(180, migrated.VirtualDisplay.Dpi,
                "first device profile must preserve existing DPI");
            Equal("30M", migrated.Scrcpy.BitRate,
                "first device profile must preserve existing bitrate");
            Equal(1200, migrated.SingleWindowSlots[1].Width,
                "first device profile must preserve existing slot width");
            Equal(800, migrated.SingleWindowSlots[1].Height,
                "first device profile must preserve existing slot height");
        }

        private static void PersistsDeviceRunSettingsProfiles()
        {
            var settings = AppSettings.CreateDefault();
            var phoneA = settings.GetOrCreateDeviceRunSettings("phone-a");
            var phoneB = settings.GetOrCreateDeviceRunSettings("phone-b");
            phoneA.VirtualDisplay.Width = 1920;
            phoneB.VirtualDisplay.Width = 1280;
            phoneA.SingleWindowSlots[2].StartAppPackage = "app.a";
            phoneB.SingleWindowSlots[2].StartAppPackage = "app.b";

            var serializer = new DataContractJsonSerializer(
                typeof(AppSettings));
            AppSettings loaded;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, settings);
                stream.Position = 0;
                loaded = (AppSettings)serializer.ReadObject(stream);
            }
            loaded.EnsureDefaults();

            var loadedA = loaded.GetOrCreateDeviceRunSettings("phone-a");
            var loadedB = loaded.GetOrCreateDeviceRunSettings("phone-b");
            Equal(1920, loadedA.VirtualDisplay.Width,
                "phone-a profile must survive settings serialization");
            Equal(1280, loadedB.VirtualDisplay.Width,
                "phone-b profile must survive settings serialization");
            Equal("app.a", loadedA.SingleWindowSlots[2].StartAppPackage,
                "phone-a app selection must survive serialization");
            Equal("app.b", loadedB.SingleWindowSlots[2].StartAppPackage,
                "phone-b app selection must survive serialization");
        }

        private static void KeepsWirelessSettingsIndependentPerPhysicalDevice()
        {
            var settings = AppSettings.CreateDefault();
            var phoneA = settings.GetOrCreateDeviceWirelessConnection(
                "phone-a",
                false);
            var phoneB = settings.GetOrCreateDeviceWirelessConnection(
                "phone-b",
                false);
            phoneA.Mode = AdbConnectionMode.Wireless;
            phoneA.WirelessHost = "192.168.50.82";
            phoneA.WirelessPort = 5555;
            phoneA.AutoReconnect = true;

            Equal(AdbConnectionMode.Usb, phoneB.Mode,
                "phone-b mode must not follow phone-a");
            True(string.IsNullOrEmpty(phoneB.WirelessHost),
                "phone-b address must not follow phone-a");
            Equal(5555, phoneB.WirelessPort,
                "phone-b must retain the default wireless port");
        }

        private static void SeedsSelectedWirelessDeviceFromLegacyConnection()
        {
            var settings = AppSettings.CreateDefault();
            settings.Connection.Mode = AdbConnectionMode.Wireless;
            settings.Connection.WirelessHost = "192.168.50.83";
            settings.Connection.WirelessPort = 5566;
            settings.Connection.AutoReconnect = false;

            var migrated = settings.GetOrCreateDeviceWirelessConnection(
                "existing-phone",
                true);
            var other = settings.GetOrCreateDeviceWirelessConnection(
                "other-phone",
                false);

            Equal(AdbConnectionMode.Wireless, migrated.Mode,
                "selected legacy device must preserve wireless mode");
            Equal("192.168.50.83", migrated.WirelessHost,
                "selected legacy device must preserve its address");
            Equal(5566, migrated.WirelessPort,
                "selected legacy device must preserve its port");
            True(!migrated.AutoReconnect,
                "selected legacy device must preserve reconnect setting");
            True(string.IsNullOrEmpty(other.WirelessHost),
                "another physical device must not inherit legacy address");
        }

        private static void PersistsDeviceWirelessConnectionProfiles()
        {
            var settings = AppSettings.CreateDefault();
            var phoneA = settings.GetOrCreateDeviceWirelessConnection(
                "phone-a",
                false);
            var phoneB = settings.GetOrCreateDeviceWirelessConnection(
                "phone-b",
                false);
            phoneA.Mode = AdbConnectionMode.Wireless;
            phoneA.WirelessHost = "10.0.0.2";
            phoneA.WirelessPort = 5555;
            phoneB.Mode = AdbConnectionMode.Wireless;
            phoneB.WirelessHost = "10.0.0.3";
            phoneB.WirelessPort = 5566;

            var serializer = new DataContractJsonSerializer(
                typeof(AppSettings));
            AppSettings loaded;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, settings);
                stream.Position = 0;
                loaded = (AppSettings)serializer.ReadObject(stream);
            }
            loaded.EnsureDefaults();

            var loadedA = loaded.FindDeviceWirelessConnection("phone-a");
            var loadedB = loaded.FindDeviceWirelessConnection("phone-b");
            NotNull(loadedA,
                "phone-a wireless profile must survive serialization");
            NotNull(loadedB,
                "phone-b wireless profile must survive serialization");
            Equal("10.0.0.2", loadedA.WirelessHost,
                "phone-a address must survive serialization");
            Equal(5566, loadedB.WirelessPort,
                "phone-b port must survive serialization");
        }

        private static void RoundTripsCompanionGuardianProtocolPayload()
        {
            const string expected = "0|한글·Unicode/150";
            using (var stream = new MemoryStream())
            {
                CompanionGuardianProtocol.WriteInt32(
                    stream, CompanionGuardianProtocol.Magic);
                CompanionGuardianProtocol.WriteString(stream, expected);
                stream.Position = 0;

                Equal(
                    CompanionGuardianProtocol.Magic,
                    CompanionGuardianProtocol.ReadInt32(stream),
                    "guardian protocol must preserve the big-endian magic");
                Equal(
                    expected,
                    CompanionGuardianProtocol.ReadString(stream),
                    "guardian protocol must preserve Unicode payloads");
            }
        }

        private static void BlocksNewProcessesAfterShutdown()
        {
            var runner = new ProcessRunner(new LogService());
            runner.BeginShutdown();

            var result = runner.Run(
                GetCommandProcessorPath(),
                GetCommandProcessorArgs(),
                null,
                5000,
                false);

            True(result.Canceled,
                "shutdown gate must cancel new child process launches");
            True(!result.IsSuccess,
                "a shutdown-canceled process must not report success");
        }

        private static void TerminatesActiveProcessOnShutdown()
        {
            var runner = new ProcessRunner(new LogService());
            var task = Task.Run(delegate
            {
                return runner.Run(
                    GetPingExecutablePath(),
                    GetPingArguments(),
                    null,
                    30000,
                    false);
            });

            Thread.Sleep(300);
            runner.BeginShutdown();

            True(task.Wait(5000),
                "active child process must end promptly during shutdown");
            True(task.Result.Canceled,
                "terminated child process must report shutdown cancellation");
        }

        private static void TerminatesOnlyConfiguredBundledExecutablePath()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "DXManager.ProcessCleanup." + Guid.NewGuid().ToString("N"));
            var firstDirectory = Path.Combine(root, "owned");
            var secondDirectory = Path.Combine(root, "unrelated");
            Directory.CreateDirectory(firstDirectory);
            Directory.CreateDirectory(secondDirectory);

            var executableName = "dxmtest" +
                Guid.NewGuid().ToString("N").Substring(0, 8) + (OperatingSystem.IsWindows() ? ".exe" : "");
            var firstPath = Path.Combine(firstDirectory, executableName);
            var secondPath = Path.Combine(secondDirectory, executableName);

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.WriteAllText(firstPath, "#!/bin/sh\nsleep 30\n");
                File.WriteAllText(secondPath, "#!/bin/sh\nsleep 30\n");
                File.SetUnixFileMode(firstPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                File.SetUnixFileMode(secondPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            else
            {
                File.Copy(GetPingExecutablePath(), firstPath);
                File.Copy(GetPingExecutablePath(), secondPath);
            }

            Process first = null;
            Process second = null;
            try
            {
                first = Process.Start(new ProcessStartInfo
                {
                    FileName = firstPath,
                    Arguments = OperatingSystem.IsWindows() ? GetPingArguments() : "",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                second = Process.Start(new ProcessStartInfo
                {
                    FileName = secondPath,
                    Arguments = OperatingSystem.IsWindows() ? GetPingArguments() : "",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                Thread.Sleep(200);
                var cleanup = new BundledProcessCleanupService(
                    new LogService());
                cleanup.AddExecutablePath(firstPath);
                Equal(1, cleanup.TerminateRemainingProcesses(),
                    "only the configured bundled executable must terminate");

                True(first.WaitForExit(3000),
                    "configured executable must exit during final cleanup");
                True(!second.HasExited,
                    "same-named executable in another path must remain alive");
            }
            finally
            {
                if (first != null)
                {
                    try { if (!first.HasExited) first.Kill(); }
                    catch { }
                    first.Dispose();
                }
                if (second != null)
                {
                    try
                    {
                        if (!second.HasExited)
                        {
                            second.Kill();
                            second.WaitForExit(3000);
                        }
                    }
                    catch { }
                    second.Dispose();
                }
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static void SerializesConcurrentSettingsSaves()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "DXManager.Settings." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                const int writerCount = 8;
                var gate = new ManualResetEventSlim(false);
                var tasks = new Task[writerCount];
                for (var index = 0; index < writerCount; index++)
                {
                    var writer = index;
                    tasks[index] = Task.Run(delegate
                    {
                        var service = new SettingsService(
                            new LogService(),
                            root);
                        var settings = AppSettings.CreateDefault();
                        settings.Connection.WirelessHost =
                            "192.168.0." + (writer + 1);
                        gate.Wait();
                        service.Save(settings);
                    });
                }

                gate.Set();
                True(Task.WaitAll(tasks, 10000),
                    "concurrent settings saves must finish promptly");

                var verifier = new SettingsService(new LogService(), root);
                var loaded = verifier.Load();
                NotNull(loaded,
                    "concurrent settings saves must leave valid settings");
                True(File.Exists(verifier.SettingsFilePath),
                    "concurrent settings saves must create settings.json");
                Equal(
                    0,
                    Directory.GetFiles(
                        Path.GetDirectoryName(verifier.SettingsFilePath),
                        "settings.json.*.tmp").Length,
                    "successful settings saves must remove unique temp files");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static string GetCommandProcessorPath()
        {
            if (OperatingSystem.IsWindows()) return GetSystemExecutablePath("cmd.exe");
            return "/bin/sh";
        }

        private static string GetCommandProcessorArgs()
        {
            if (OperatingSystem.IsWindows()) return "/d /c exit 0";
            return "-c \"exit 0\"";
        }

        private static string GetPingExecutablePath()
        {
            if (OperatingSystem.IsWindows()) return GetSystemExecutablePath("ping.exe");
            if (File.Exists("/sbin/ping")) return "/sbin/ping";
            if (File.Exists("/bin/ping")) return "/bin/ping";
            if (File.Exists("/usr/bin/ping")) return "/usr/bin/ping";
            return "/bin/sleep";
        }

        private static string GetPingArguments()
        {
            if (OperatingSystem.IsWindows()) return "127.0.0.1 -n 30";
            var exe = GetPingExecutablePath();
            if (exe.Contains("sleep")) return "30";
            return "-c 30 127.0.0.1";
        }

        private static string GetSystemExecutablePath(string fileName)
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    fileName);
            }
            if (fileName.StartsWith("ping", StringComparison.OrdinalIgnoreCase)) return GetPingExecutablePath();
            if (fileName.StartsWith("cmd", StringComparison.OrdinalIgnoreCase)) return "/bin/sh";
            return Path.Combine("/bin", fileName);
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

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + "; expected=" + expected + ", actual=" + actual);
            }
        }

        private static void NotNull(object value, string message)
        {
            if (value == null) throw new InvalidOperationException(message);
        }

        private static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Throws<T>(Action action, string message)
            where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }
    }
}
