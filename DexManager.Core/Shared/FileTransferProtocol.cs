using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DexManager.FileTransfer
{
    internal static class FileTransferEnvironment
    {
        internal const string RealAdbPath = "DXM_REAL_ADB";
        internal const string PipeName = "DXM_TRANSFER_PIPE";
        internal const string PipeToken = "DXM_TRANSFER_TOKEN";
        internal const string SessionId = "DXM_TRANSFER_SESSION";
        internal const string SessionSerial = "DXM_TRANSFER_SERIAL";
        internal const string RemoteDirectory = "DXM_TRANSFER_TARGET";
        internal const string Enabled = "DXM_TRANSFER_ENABLED";
        internal const string DefaultRemoteDirectory = "/sdcard/Download/";
        internal const int ProtocolVersion = 1;
        internal const int MaximumMessageBytes = 64 * 1024;
    }

    [DataContract]
    internal sealed class FileTransferRequestMessage
    {
        [DataMember(Order = 1)] public int Version { get; set; }
        [DataMember(Order = 2)] public string Token { get; set; }
        [DataMember(Order = 3)] public string SessionId { get; set; }
        [DataMember(Order = 4)] public string RequestId { get; set; }
        [DataMember(Order = 5)] public string Serial { get; set; }
        [DataMember(Order = 6)] public string LocalPath { get; set; }
        [DataMember(Order = 7)] public string RemoteDirectory { get; set; }
    }

    [DataContract]
    internal sealed class FileTransferResponseMessage
    {
        [DataMember(Order = 1)] public int Version { get; set; }
        [DataMember(Order = 2)] public bool Success { get; set; }
        [DataMember(Order = 3)] public bool Canceled { get; set; }
        [DataMember(Order = 4)] public int ExitCode { get; set; }
        [DataMember(Order = 5)] public string Message { get; set; }
        [DataMember(Order = 6)] public string FinalFileName { get; set; }
    }

    internal static class FileTransferWire
    {
        internal static void Write<T>(Stream stream, T message)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            using (var payload = new MemoryStream())
            {
                CreateSerializer<T>().WriteObject(payload, message);
                if (payload.Length > FileTransferEnvironment.MaximumMessageBytes)
                    throw new InvalidDataException("File transfer IPC message is too large.");

                var length = BitConverter.GetBytes((int)payload.Length);
                stream.Write(length, 0, length.Length);
                payload.Position = 0;
                payload.CopyTo(stream);
                stream.Flush();
            }
        }

        internal static T Read<T>(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            var lengthBytes = ReadExactly(stream, sizeof(int));
            var length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= 0 || length > FileTransferEnvironment.MaximumMessageBytes)
                throw new InvalidDataException("Invalid file transfer IPC message length.");

            var payload = ReadExactly(stream, length);
            using (var memory = new MemoryStream(payload, false))
            {
                return (T)CreateSerializer<T>().ReadObject(memory);
            }
        }

        private static DataContractJsonSerializer CreateSerializer<T>()
        {
            return new DataContractJsonSerializer(typeof(T));
        }

        private static byte[] ReadExactly(Stream stream, int count)
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

    internal static class WindowsCommandLine
    {
        internal static string Build(IEnumerable<string> arguments)
        {
            if (arguments == null) return string.Empty;
            var builder = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(Quote(argument));
            }
            return builder.ToString();
        }

        internal static string Quote(string value)
        {
            value = value ?? string.Empty;
            if (value.Length > 0 &&
                value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return value;
            }

            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }

                if (backslashes > 0)
                {
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                }
                builder.Append(character);
            }

            if (backslashes > 0) builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
