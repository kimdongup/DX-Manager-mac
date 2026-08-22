using System;
using System.IO;
using DexManager.FileTransfer;
using Xunit;

namespace DexManager.Tests
{
    public class FileTransferProtocolTests
    {
        [Fact]
        public void EnvironmentConstants_HaveExpectedValues()
        {
            Assert.Equal("DXM_REAL_ADB", FileTransferEnvironment.RealAdbPath);
            Assert.Equal("DXM_TRANSFER_PIPE", FileTransferEnvironment.PipeName);
            Assert.Equal("DXM_TRANSFER_TOKEN", FileTransferEnvironment.PipeToken);
            Assert.Equal("DXM_TRANSFER_SESSION", FileTransferEnvironment.SessionId);
            Assert.Equal("DXM_TRANSFER_SERIAL", FileTransferEnvironment.SessionSerial);
            Assert.Equal("DXM_TRANSFER_TARGET", FileTransferEnvironment.RemoteDirectory);
            Assert.Equal("DXM_TRANSFER_ENABLED", FileTransferEnvironment.Enabled);
            Assert.Equal("/sdcard/Download/", FileTransferEnvironment.DefaultRemoteDirectory);
            Assert.Equal(1, FileTransferEnvironment.ProtocolVersion);
            Assert.Equal(64 * 1024, FileTransferEnvironment.MaximumMessageBytes);
        }

        [Fact]
        public void FileTransferWire_RoundTripsRequestMessage()
        {
            var original = new FileTransferRequestMessage
            {
                Version = 1,
                Token = "test-token-12345",
                SessionId = "session-abc",
                RequestId = Guid.NewGuid().ToString("N"),
                Serial = "USB-DEVICE-001",
                LocalPath = "/Users/mac/Downloads/한국어 문서.pdf",
                RemoteDirectory = "/sdcard/Download/"
            };

            using var stream = new MemoryStream();
            FileTransferWire.Write(stream, original);
            stream.Position = 0;

            var result = FileTransferWire.Read<FileTransferRequestMessage>(stream);

            Assert.NotNull(result);
            Assert.Equal(original.Version, result.Version);
            Assert.Equal(original.Token, result.Token);
            Assert.Equal(original.SessionId, result.SessionId);
            Assert.Equal(original.RequestId, result.RequestId);
            Assert.Equal(original.Serial, result.Serial);
            Assert.Equal(original.LocalPath, result.LocalPath);
            Assert.Equal(original.RemoteDirectory, result.RemoteDirectory);
        }

        [Fact]
        public void FileTransferWire_RoundTripsResponseMessage()
        {
            var original = new FileTransferResponseMessage
            {
                Version = 1,
                Success = true,
                Canceled = false,
                ExitCode = 0,
                Message = "전송 완료 / Transfer succeeded",
                FinalFileName = "한국어 문서 (1).pdf"
            };

            using var stream = new MemoryStream();
            FileTransferWire.Write(stream, original);
            stream.Position = 0;

            var result = FileTransferWire.Read<FileTransferResponseMessage>(stream);

            Assert.NotNull(result);
            Assert.Equal(original.Version, result.Version);
            Assert.True(result.Success);
            Assert.False(result.Canceled);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(original.Message, result.Message);
            Assert.Equal(original.FinalFileName, result.FinalFileName);
        }

        [Fact]
        public void FileTransferWire_Write_ThrowsOnNullStream()
        {
            var msg = new FileTransferRequestMessage { Version = 1 };
            Assert.Throws<ArgumentNullException>(() => FileTransferWire.Write(null, msg));
        }

        [Fact]
        public void FileTransferWire_Read_ThrowsOnNullStream()
        {
            Assert.Throws<ArgumentNullException>(() => FileTransferWire.Read<FileTransferRequestMessage>(null));
        }

        [Fact]
        public void FileTransferWire_Write_ThrowsWhenMessageExceedsMaximumBytes()
        {
            var largeMessage = new FileTransferRequestMessage
            {
                Version = 1,
                LocalPath = new string('A', FileTransferEnvironment.MaximumMessageBytes + 100)
            };

            using var stream = new MemoryStream();
            Assert.Throws<InvalidDataException>(() => FileTransferWire.Write(stream, largeMessage));
        }

        [Fact]
        public void FileTransferWire_Read_ThrowsOnInvalidLengthPrefix()
        {
            // Negative length
            using (var stream = new MemoryStream())
            {
                stream.Write(BitConverter.GetBytes(-5), 0, 4);
                stream.Position = 0;
                Assert.Throws<InvalidDataException>(() => FileTransferWire.Read<FileTransferRequestMessage>(stream));
            }

            // Zero length
            using (var stream = new MemoryStream())
            {
                stream.Write(BitConverter.GetBytes(0), 0, 4);
                stream.Position = 0;
                Assert.Throws<InvalidDataException>(() => FileTransferWire.Read<FileTransferRequestMessage>(stream));
            }

            // Excessively large length
            using (var stream = new MemoryStream())
            {
                stream.Write(BitConverter.GetBytes(FileTransferEnvironment.MaximumMessageBytes + 1), 0, 4);
                stream.Position = 0;
                Assert.Throws<InvalidDataException>(() => FileTransferWire.Read<FileTransferRequestMessage>(stream));
            }
        }

        [Fact]
        public void FileTransferWire_Read_ThrowsOnTruncatedStream()
        {
            using var stream = new MemoryStream();
            stream.Write(BitConverter.GetBytes(100), 0, 4);
            stream.Write(new byte[10], 0, 10); // only 10 bytes instead of 100
            stream.Position = 0;

            Assert.Throws<EndOfStreamException>(() => FileTransferWire.Read<FileTransferRequestMessage>(stream));
        }

        [Fact]
        public void WindowsCommandLine_Build_QuotesArgumentsWithSpaces()
        {
            var args = new[] { "adb", "push", "/Users/mac/My File.txt", "/sdcard/Download/" };
            var result = WindowsCommandLine.Build(args);

            Assert.Equal("adb push \"/Users/mac/My File.txt\" /sdcard/Download/", result);
        }

        [Fact]
        public void WindowsCommandLine_Build_EscapesQuotesAndBackslashes()
        {
            var args = new[] { "echo", "hello \"world\"", @"C:\Program Files\" };
            var result = WindowsCommandLine.Build(args);

            Assert.Contains("\\\"world\\\"", result);
            Assert.Contains(@"C:\Program Files\\""", result);
        }

        [Fact]
        public void WindowsCommandLine_Build_ReturnsEmptyOnNull()
        {
            Assert.Equal(string.Empty, WindowsCommandLine.Build(null));
        }
    }
}
