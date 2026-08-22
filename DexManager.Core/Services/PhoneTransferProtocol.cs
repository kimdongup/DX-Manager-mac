using System;
using System.IO;
using System.Text;

namespace DexManager.Services
{
    internal static class PhoneTransferProtocol
    {
        public const int Magic = 0x44584D52; // DXMR
        public const int Version = 1;
        public const int MaxItemCount = 100000;
        public const int MaxStringBytes = 1024 * 1024;
        public const int MaxChunkBytes = 1024 * 1024;
        public const string StatusProbeBatch = "__DXM_STATUS_PROBE__";

        public static int ReadInt32(Stream stream)
        {
            var buffer = ReadExact(stream, 4);
            return (buffer[0] << 24) |
                (buffer[1] << 16) |
                (buffer[2] << 8) |
                buffer[3];
        }

        public static long ReadInt64(Stream stream)
        {
            var high = (uint)ReadInt32(stream);
            var low = (uint)ReadInt32(stream);
            return ((long)high << 32) | low;
        }

        public static string ReadString(Stream stream)
        {
            var length = ReadInt32(stream);
            if (length < 0 || length > MaxStringBytes)
                throw new InvalidDataException("Invalid UTF-8 string length.");
            return Encoding.UTF8.GetString(ReadExact(stream, length));
        }

        public static void WriteResponse(
            Stream stream,
            bool success,
            string message)
        {
            stream.WriteByte(success ? (byte)1 : (byte)0);
            WriteString(stream, message ?? string.Empty);
            stream.Flush();
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteInt32(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteInt32(Stream stream, int value)
        {
            var buffer = new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
            stream.Write(buffer, 0, buffer.Length);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
            return buffer;
        }
    }
}
