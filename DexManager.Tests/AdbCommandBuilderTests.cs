using System;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public class AdbCommandBuilderTests
    {
        [Fact]
        public void ForDevice_ThrowsOnNullOrEmptySerial()
        {
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForDevice(null, "shell get-state"));
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForDevice(string.Empty, "shell get-state"));
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForDevice("   ", "shell get-state"));
        }

        [Fact]
        public void ForDevice_ThrowsOnNullOrEmptyArguments()
        {
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForDevice("DEVICE1", null));
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForDevice("DEVICE1", string.Empty));
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForDevice("DEVICE1", "   "));
        }

        [Fact]
        public void ForDevice_QuotesSerialCorrectly()
        {
            var cmd = AdbCommandBuilder.ForDevice("PHONE-A", "shell getprop ro.product.model");
            Assert.Equal("-s \"PHONE-A\" shell getprop ro.product.model", cmd);
        }

        [Fact]
        public void ForDevice_EscapesQuotesInSerial()
        {
            var cmd = AdbCommandBuilder.ForDevice("PHONE\"A", "shell get-state");
            Assert.Equal("-s \"PHONE\\\"A\" shell get-state", cmd);
        }

        [Fact]
        public void ForDevice_TrimsSerial()
        {
            var cmd = AdbCommandBuilder.ForDevice("  PHONE-1  ", "devices");
            Assert.Equal("-s \"PHONE-1\" devices", cmd);
        }

        [Fact]
        public void ForDevice_KeepsMultipleDevicesIsolated()
        {
            var first = AdbCommandBuilder.ForDevice("PHONE-A", "shell settings delete global overlay_display_devices");
            var second = AdbCommandBuilder.ForDevice("PHONE-B", "shell settings delete global overlay_display_devices");

            Assert.StartsWith("-s \"PHONE-A\" ", first);
            Assert.DoesNotContain("PHONE-B", first);

            Assert.StartsWith("-s \"PHONE-B\" ", second);
            Assert.DoesNotContain("PHONE-A", second);
        }

        [Fact]
        public void ForDevice_InterleavedCommandsDoNotShareTarget()
        {
            for (var i = 0; i < 500; i++)
            {
                var serial = i % 2 == 0 ? "DEVICE_ALPHA" : "DEVICE_BETA";
                var other = i % 2 == 0 ? "DEVICE_BETA" : "DEVICE_ALPHA";
                var cmd = AdbCommandBuilder.ForDevice(serial, $"shell echo {i}");

                Assert.StartsWith($"-s \"{serial}\" ", cmd);
                Assert.DoesNotContain(other, cmd);
            }
        }

        [Fact]
        public void ForShellCommands_ThrowsOnNullOrEmptyArray()
        {
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForShellCommands("DEVICE1", null));
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForShellCommands("DEVICE1", Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => AdbCommandBuilder.ForShellCommands("DEVICE1", "  ", null));
        }

        [Fact]
        public void ForShellCommands_CombinesMultipleCommandsWithSemicolon()
        {
            var cmd = AdbCommandBuilder.ForShellCommands(
                "PHONE-A",
                "settings delete global overlay_display_devices",
                "settings put global stay_on_while_plugged_in 3");

            Assert.Equal(
                "-s \"PHONE-A\" shell \"settings delete global overlay_display_devices; settings put global stay_on_while_plugged_in 3\"",
                cmd);
        }

        [Fact]
        public void ForShellCommands_IgnoresEmptyEntries()
        {
            var cmd = AdbCommandBuilder.ForShellCommands(
                "PHONE-A",
                "echo hello",
                "  ",
                "echo world");

            Assert.Equal("-s \"PHONE-A\" shell \"echo hello; echo world\"", cmd);
        }

        [Fact]
        public void ForShellCommands_EscapesQuotesInCommands()
        {
            var cmd = AdbCommandBuilder.ForShellCommands(
                "PHONE-A",
                "echo \"test message\"",
                "getprop \"ro.build.version.release\"");

            Assert.Equal(
                "-s \"PHONE-A\" shell \"echo \\\"test message\\\"; getprop \\\"ro.build.version.release\\\"\"",
                cmd);
        }

        [Fact]
        public void DeviceSerialScope_MatchesCorrectly()
        {
            Assert.True(DeviceSerialScope.Matches("PHONE-A", "phone-a"));
            Assert.True(DeviceSerialScope.Matches("phone-a", "PHONE-A"));
            Assert.False(DeviceSerialScope.Matches("PHONE-A", "PHONE-B"));
            Assert.False(DeviceSerialScope.Matches(string.Empty, "PHONE-A"));
            Assert.False(DeviceSerialScope.Matches("PHONE-A", string.Empty));
            Assert.False(DeviceSerialScope.Matches(null, "PHONE-A"));
            Assert.False(DeviceSerialScope.Matches("PHONE-A", null));
        }
    }
}
