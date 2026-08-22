using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using DexManager.Models;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public class AppSettingsTests
    {
        [Fact]
        public void KeepsRunSettingsIndependentPerPhysicalDevice()
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

            Assert.Equal(1600, phoneB.VirtualDisplay.Width);
            Assert.Equal(900, phoneB.VirtualDisplay.Height);
            Assert.Equal(150, phoneB.VirtualDisplay.Dpi);
            Assert.Equal(1600, phoneB.SingleWindowSlots[0].Width);
            Assert.True(string.IsNullOrEmpty(phoneB.SingleWindowSlots[0].StartAppPackage));
            Assert.Equal("8M", phoneB.Scrcpy.BitRate);
        }

        [Fact]
        public void SeedsNewDeviceSettingsFromLegacyTemplate()
        {
            var settings = AppSettings.CreateDefault();
            settings.VirtualDisplay.Width = 2560;
            settings.VirtualDisplay.Height = 1440;
            settings.VirtualDisplay.Dpi = 180;
            settings.Scrcpy.BitRate = "30M";
            settings.SingleWindowSlots[1].Width = 1200;
            settings.SingleWindowSlots[1].Height = 800;

            var migrated = settings.GetOrCreateDeviceRunSettings("existing-phone");

            Assert.Equal(2560, migrated.VirtualDisplay.Width);
            Assert.Equal(1440, migrated.VirtualDisplay.Height);
            Assert.Equal(180, migrated.VirtualDisplay.Dpi);
            Assert.Equal("30M", migrated.Scrcpy.BitRate);
            Assert.Equal(1200, migrated.SingleWindowSlots[1].Width);
            Assert.Equal(800, migrated.SingleWindowSlots[1].Height);
        }

        [Fact]
        public void PersistsDeviceRunSettingsProfiles()
        {
            var settings = AppSettings.CreateDefault();
            var phoneA = settings.GetOrCreateDeviceRunSettings("phone-a");
            var phoneB = settings.GetOrCreateDeviceRunSettings("phone-b");
            phoneA.VirtualDisplay.Width = 1920;
            phoneB.VirtualDisplay.Width = 1280;
            phoneA.SingleWindowSlots[2].StartAppPackage = "app.a";
            phoneB.SingleWindowSlots[2].StartAppPackage = "app.b";

            var serializer = new DataContractJsonSerializer(typeof(AppSettings));
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
            Assert.Equal(1920, loadedA.VirtualDisplay.Width);
            Assert.Equal(1280, loadedB.VirtualDisplay.Width);
            Assert.Equal("app.a", loadedA.SingleWindowSlots[2].StartAppPackage);
            Assert.Equal("app.b", loadedB.SingleWindowSlots[2].StartAppPackage);
        }

        [Fact]
        public void KeepsWirelessSettingsIndependentPerPhysicalDevice()
        {
            var settings = AppSettings.CreateDefault();
            var phoneA = settings.GetOrCreateDeviceWirelessConnection("phone-a", false);
            var phoneB = settings.GetOrCreateDeviceWirelessConnection("phone-b", false);
            phoneA.Mode = AdbConnectionMode.Wireless;
            phoneA.WirelessHost = "192.168.50.82";
            phoneA.WirelessPort = 5555;
            phoneA.AutoReconnect = true;

            Assert.Equal(AdbConnectionMode.Usb, phoneB.Mode);
            Assert.True(string.IsNullOrEmpty(phoneB.WirelessHost));
            Assert.Equal(5555, phoneB.WirelessPort);
        }

        [Fact]
        public void SeedsSelectedWirelessDeviceFromLegacyConnection()
        {
            var settings = AppSettings.CreateDefault();
            settings.Connection.Mode = AdbConnectionMode.Wireless;
            settings.Connection.WirelessHost = "192.168.50.83";
            settings.Connection.WirelessPort = 5566;
            settings.Connection.AutoReconnect = false;

            var migrated = settings.GetOrCreateDeviceWirelessConnection("existing-phone", true);
            var other = settings.GetOrCreateDeviceWirelessConnection("other-phone", false);

            Assert.Equal(AdbConnectionMode.Wireless, migrated.Mode);
            Assert.Equal("192.168.50.83", migrated.WirelessHost);
            Assert.Equal(5566, migrated.WirelessPort);
            Assert.False(migrated.AutoReconnect);
            Assert.True(string.IsNullOrEmpty(other.WirelessHost));
        }

        [Fact]
        public void PersistsDeviceWirelessConnectionProfiles()
        {
            var settings = AppSettings.CreateDefault();
            var phoneA = settings.GetOrCreateDeviceWirelessConnection("phone-a", false);
            var phoneB = settings.GetOrCreateDeviceWirelessConnection("phone-b", false);
            phoneA.Mode = AdbConnectionMode.Wireless;
            phoneA.WirelessHost = "10.0.0.2";
            phoneA.WirelessPort = 5555;
            phoneB.Mode = AdbConnectionMode.Wireless;
            phoneB.WirelessHost = "10.0.0.3";
            phoneB.WirelessPort = 5566;

            var serializer = new DataContractJsonSerializer(typeof(AppSettings));
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
            Assert.NotNull(loadedA);
            Assert.NotNull(loadedB);
            Assert.Equal("10.0.0.2", loadedA.WirelessHost);
            Assert.Equal(5566, loadedB.WirelessPort);
        }

        [Fact]
        public async Task SerializesConcurrentSettingsSaves()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "DXManager.SettingsTest." + Guid.NewGuid().ToString("N"));
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
                        var service = new SettingsService(new LogService(), root);
                        var settings = AppSettings.CreateDefault();
                        settings.Connection.WirelessHost = "192.168.0." + (writer + 1);
                        gate.Wait();
                        service.Save(settings);
                    });
                }

                gate.Set();
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

                var verifier = new SettingsService(new LogService(), root);
                var loaded = verifier.Load();
                Assert.NotNull(loaded);
                Assert.True(File.Exists(verifier.SettingsFilePath));
                Assert.Empty(Directory.GetFiles(
                    Path.GetDirectoryName(verifier.SettingsFilePath),
                    "settings.json.*.tmp"));
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }
    }
}
