using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public class CompanionAndPhoneTransferProtocolTests
    {
        [Fact]
        public void CompanionGuardianProtocol_Constants_AreCorrect()
        {
            Assert.Equal(0x44584744, CompanionGuardianProtocol.Magic);
            Assert.Equal(1, CompanionGuardianProtocol.Version);
            Assert.Equal((byte)1, CompanionGuardianProtocol.Ping);
            Assert.Equal((byte)2, CompanionGuardianProtocol.WindowsShutdown);
            Assert.Equal((byte)3, CompanionGuardianProtocol.StopMonitoring);
            Assert.Equal(64 * 1024, CompanionGuardianProtocol.MaxStringBytes);
        }

        [Fact]
        public void CompanionGuardianProtocol_WriteAndReadInt32_UsesBigEndian()
        {
            using var stream = new MemoryStream();
            CompanionGuardianProtocol.WriteInt32(stream, 0x12345678);
            stream.Position = 0;

            var bytes = stream.ToArray();
            Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, bytes);

            stream.Position = 0;
            var readValue = CompanionGuardianProtocol.ReadInt32(stream);
            Assert.Equal(0x12345678, readValue);
        }

        [Fact]
        public void CompanionGuardianProtocol_RoundTripsUnicodeStrings()
        {
            const string expected = "0|한글·Unicode/150 - 특수문자 #@!🚀";
            using var stream = new MemoryStream();
            CompanionGuardianProtocol.WriteInt32(stream, CompanionGuardianProtocol.Magic);
            CompanionGuardianProtocol.WriteString(stream, expected);
            stream.Position = 0;

            Assert.Equal(CompanionGuardianProtocol.Magic, CompanionGuardianProtocol.ReadInt32(stream));
            Assert.Equal(expected, CompanionGuardianProtocol.ReadString(stream));
        }

        [Fact]
        public void CompanionGuardianProtocol_WriteString_HandlesNull()
        {
            using var stream = new MemoryStream();
            CompanionGuardianProtocol.WriteString(stream, null);
            stream.Position = 0;

            var result = CompanionGuardianProtocol.ReadString(stream);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void CompanionGuardianProtocol_ReadString_ThrowsOnLengthExceeded()
        {
            using var stream = new MemoryStream();
            CompanionGuardianProtocol.WriteInt32(stream, CompanionGuardianProtocol.MaxStringBytes + 1);
            stream.Position = 0;

            Assert.Throws<InvalidDataException>(() => CompanionGuardianProtocol.ReadString(stream));
        }

        [Fact]
        public void CompanionGuardianProtocol_ReadString_ThrowsOnNegativeLength()
        {
            using var stream = new MemoryStream();
            CompanionGuardianProtocol.WriteInt32(stream, -1);
            stream.Position = 0;

            Assert.Throws<InvalidDataException>(() => CompanionGuardianProtocol.ReadString(stream));
        }

        [Fact]
        public void CompanionGuardianProtocol_ReadString_ThrowsOnTruncatedStream()
        {
            using var stream = new MemoryStream();
            CompanionGuardianProtocol.WriteInt32(stream, 50);
            stream.Write(new byte[10], 0, 10);
            stream.Position = 0;

            Assert.Throws<EndOfStreamException>(() => CompanionGuardianProtocol.ReadString(stream));
        }

        [Fact]
        public void PhoneTransferProtocol_Constants_AreCorrect()
        {
            Assert.Equal(0x44584D52, PhoneTransferProtocol.Magic);
            Assert.Equal(1, PhoneTransferProtocol.Version);
            Assert.Equal(100000, PhoneTransferProtocol.MaxItemCount);
            Assert.Equal(1024 * 1024, PhoneTransferProtocol.MaxStringBytes);
            Assert.Equal(1024 * 1024, PhoneTransferProtocol.MaxChunkBytes);
            Assert.Equal("__DXM_STATUS_PROBE__", PhoneTransferProtocol.StatusProbeBatch);
        }

        [Fact]
        public void PhoneTransferProtocol_ReadInt64_CombinesHighAndLowWordsCorrectly()
        {
            using var stream = new MemoryStream();
            const long testValue = 0x0123456789ABCDEFL;
            var high = (int)(testValue >> 32);
            var low = unchecked((int)testValue);

            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                // Write high and low as big endian
                stream.WriteByte((byte)(high >> 24));
                stream.WriteByte((byte)(high >> 16));
                stream.WriteByte((byte)(high >> 8));
                stream.WriteByte((byte)high);

                stream.WriteByte((byte)(low >> 24));
                stream.WriteByte((byte)(low >> 16));
                stream.WriteByte((byte)(low >> 8));
                stream.WriteByte((byte)low);
            }

            stream.Position = 0;
            var result = PhoneTransferProtocol.ReadInt64(stream);
            Assert.Equal(testValue, result);
        }

        [Fact]
        public void PhoneTransferProtocol_WriteResponse_FormatsBooleanAndString()
        {
            using var stream = new MemoryStream();
            PhoneTransferProtocol.WriteResponse(stream, true, "Ready to receive");
            stream.Position = 0;

            var successByte = stream.ReadByte();
            Assert.Equal(1, successByte);

            var message = PhoneTransferProtocol.ReadString(stream);
            Assert.Equal("Ready to receive", message);
        }

        [Fact]
        public void PhoneTransferProtocol_WriteResponse_HandlesFailure()
        {
            using var stream = new MemoryStream();
            PhoneTransferProtocol.WriteResponse(stream, false, "Session expired");
            stream.Position = 0;

            var successByte = stream.ReadByte();
            Assert.Equal(0, successByte);

            var message = PhoneTransferProtocol.ReadString(stream);
            Assert.Equal("Session expired", message);
        }

        [Fact]
        public void PhoneTransferReceiver_SanitizeFileName_SanitizesReservedNamesAndInvalidChars()
        {
            var sanitizeMethod = typeof(PhoneTransferReceiver).GetMethod(
                "SanitizeFileName",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(sanitizeMethod);

            Assert.Equal("_CON", (string)sanitizeMethod.Invoke(null, new object[] { "CON" }));
            Assert.Equal("_PRN.txt", (string)sanitizeMethod.Invoke(null, new object[] { "PRN.txt" }));
            Assert.Equal("_NUL", (string)sanitizeMethod.Invoke(null, new object[] { "NUL" }));
            Assert.Equal("_COM1", (string)sanitizeMethod.Invoke(null, new object[] { "COM1" }));
            Assert.Equal("_LPT1.doc", (string)sanitizeMethod.Invoke(null, new object[] { "LPT1.doc" }));

            var sanitized = (string)sanitizeMethod.Invoke(null, new object[] { "my\tphoto\nname.jpg" });
            Assert.DoesNotContain("\t", sanitized);
            Assert.DoesNotContain("\n", sanitized);

            var trailing = (string)sanitizeMethod.Invoke(null, new object[] { "  test_file.txt..  " });
            Assert.Equal("test_file.txt", trailing);

            var empty = (string)sanitizeMethod.Invoke(null, new object[] { "   ...  " });
            Assert.Equal("unnamed", empty);
        }

        [Fact]
        public void PhoneTransferReceiver_ResolveSafePath_PreventsDirectoryTraversal()
        {
            var resolveSafePathMethod = typeof(PhoneTransferReceiver).GetMethod(
                "ResolveSafePath",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(resolveSafePathMethod);

            var destination = Path.Combine(Path.GetTempPath(), "dxm_test_transfer");
            var rootNames = new Dictionary<int, string>();
            var reservedRootPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Normal relative path succeeds
            var normalPath = (string)resolveSafePathMethod.Invoke(null, new object[] {
                destination, 1, "subfolder/photo.jpg", rootNames, reservedRootPaths
            });
            Assert.StartsWith(destination, normalPath);

            // Path with parent segment .. throws TargetInvocationException wrapping InvalidDataException
            var ex = Assert.Throws<TargetInvocationException>(() => resolveSafePathMethod.Invoke(null, new object[] {
                destination, 2, "../escaped.txt", rootNames, reservedRootPaths
            }));
            Assert.IsType<InvalidDataException>(ex.InnerException);
        }
    }
}
