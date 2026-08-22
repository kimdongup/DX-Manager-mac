using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace DexManager.Utils
{
    internal static class ApkSigningCertificateReader
    {
        private const uint ApkSignatureSchemeV2Id = 0x7109871A;
        private const uint EndOfCentralDirectorySignature = 0x06054B50;
        private static readonly byte[] ApkSigningBlockMagic =
        {
            0x41, 0x50, 0x4B, 0x20, 0x53, 0x69, 0x67, 0x20,
            0x42, 0x6C, 0x6F, 0x63, 0x6B, 0x20, 0x34, 0x32
        };

        public static string ReadSingleV2CertificateSha256(string apkPath)
        {
            if (string.IsNullOrWhiteSpace(apkPath))
                throw new ArgumentException("APK path is empty.", "apkPath");

            using (var stream = new FileStream(
                apkPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                var centralDirectoryOffset = FindCentralDirectoryOffset(
                    stream,
                    reader);
                var v2Block = ReadV2Block(
                    stream,
                    reader,
                    centralDirectoryOffset);
                var certificates = ReadSignerCertificates(v2Block);
                if (certificates.Count != 1)
                    throw new InvalidDataException(
                        "The APK must contain exactly one v2 signer certificate.");

                using (var sha256 = SHA256.Create())
                {
                    return ToHex(sha256.ComputeHash(certificates[0]));
                }
            }
        }

        private static long FindCentralDirectoryOffset(
            Stream stream,
            BinaryReader reader)
        {
            const int minimumEocdSize = 22;
            const int maximumCommentSize = 65535;
            var searchLength = (int)Math.Min(
                stream.Length,
                minimumEocdSize + maximumCommentSize);
            var buffer = new byte[searchLength];
            stream.Position = stream.Length - searchLength;
            ReadExactly(stream, buffer, 0, buffer.Length);

            for (var index = buffer.Length - minimumEocdSize;
                index >= 0;
                index--)
            {
                if (ReadUInt32(buffer, index) !=
                    EndOfCentralDirectorySignature)
                {
                    continue;
                }

                var commentLength = ReadUInt16(buffer, index + 20);
                if (index + minimumEocdSize + commentLength != buffer.Length)
                    continue;

                var offset = ReadUInt32(buffer, index + 16);
                if (offset >= stream.Length)
                    throw new InvalidDataException(
                        "APK central directory offset is invalid.");
                return offset;
            }

            throw new InvalidDataException("APK ZIP footer was not found.");
        }

        private static byte[] ReadV2Block(
            Stream stream,
            BinaryReader reader,
            long centralDirectoryOffset)
        {
            if (centralDirectoryOffset < 32)
                throw new InvalidDataException(
                    "APK signing block is missing.");

            stream.Position = centralDirectoryOffset - 24;
            var footerSize = reader.ReadUInt64();
            var magic = reader.ReadBytes(ApkSigningBlockMagic.Length);
            if (!BytesEqual(magic, ApkSigningBlockMagic))
                throw new InvalidDataException(
                    "APK signing block magic is missing.");
            if (footerSize < 24 || footerSize > int.MaxValue)
                throw new InvalidDataException(
                    "APK signing block size is invalid.");

            var blockStart = centralDirectoryOffset -
                checked((long)footerSize + 8L);
            if (blockStart < 0)
                throw new InvalidDataException(
                    "APK signing block offset is invalid.");

            stream.Position = blockStart;
            var headerSize = reader.ReadUInt64();
            if (headerSize != footerSize)
                throw new InvalidDataException(
                    "APK signing block sizes do not match.");

            var pairsEnd = centralDirectoryOffset - 24;
            while (stream.Position < pairsEnd)
            {
                var pairSize = reader.ReadUInt64();
                if (pairSize < 4 || pairSize > int.MaxValue ||
                    stream.Position + (long)pairSize > pairsEnd)
                {
                    throw new InvalidDataException(
                        "APK signing block entry is invalid.");
                }

                var id = reader.ReadUInt32();
                var valueLength = checked((int)pairSize - 4);
                if (id == ApkSignatureSchemeV2Id)
                    return ReadExactly(reader, valueLength);
                stream.Position += valueLength;
            }

            throw new InvalidDataException(
                "APK Signature Scheme v2 block was not found.");
        }

        private static IList<byte[]> ReadSignerCertificates(byte[] v2Block)
        {
            using (var stream = new MemoryStream(v2Block, false))
            using (var reader = new BinaryReader(stream))
            {
                var signers = ReadLengthPrefixed(reader);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException(
                        "APK v2 signer list has trailing data.");

                var certificates = new List<byte[]>();
                using (var signerStream = new MemoryStream(signers, false))
                using (var signerReader = new BinaryReader(signerStream))
                {
                    while (signerStream.Position < signerStream.Length)
                    {
                        var signer = ReadLengthPrefixed(signerReader);
                        certificates.Add(ReadFirstCertificate(signer));
                    }
                }
                return certificates;
            }
        }

        private static byte[] ReadFirstCertificate(byte[] signer)
        {
            using (var signerStream = new MemoryStream(signer, false))
            using (var signerReader = new BinaryReader(signerStream))
            {
                var signedData = ReadLengthPrefixed(signerReader);
                ReadLengthPrefixed(signerReader); // signatures
                ReadLengthPrefixed(signerReader); // public key
                if (signerStream.Position != signerStream.Length)
                    throw new InvalidDataException(
                        "APK v2 signer has trailing data.");

                using (var dataStream = new MemoryStream(signedData, false))
                using (var dataReader = new BinaryReader(dataStream))
                {
                    ReadLengthPrefixed(dataReader); // digests
                    var certificateSequence = ReadLengthPrefixed(dataReader);
                    ReadLengthPrefixed(dataReader); // attributes
                    if (dataStream.Position != dataStream.Length)
                    {
                        var remaining = dataStream.Length -
                            dataStream.Position;
                        // Android's platform verifier consumes the three
                        // defined slices and permits remaining signed bytes.
                        // The official APK produced by apksigner 36 carries
                        // one zero uint32 suffix. Accept only that exact form
                        // rather than ignoring arbitrary trailing content.
                        if (remaining != 4 ||
                            dataReader.ReadUInt32() != 0)
                        {
                            throw new InvalidDataException(
                                "APK v2 signed data has unexpected trailing data.");
                        }
                    }

                    using (var certificateStream = new MemoryStream(
                        certificateSequence,
                        false))
                    using (var certificateReader = new BinaryReader(
                        certificateStream))
                    {
                        var first = ReadLengthPrefixed(certificateReader);
                        if (certificateStream.Position !=
                            certificateStream.Length)
                        {
                            throw new InvalidDataException(
                                "APK v2 signer has multiple certificates.");
                        }
                        return first;
                    }
                }
            }
        }

        private static byte[] ReadLengthPrefixed(BinaryReader reader)
        {
            var remaining = reader.BaseStream.Length -
                reader.BaseStream.Position;
            if (remaining < 4)
                throw new InvalidDataException(
                    "APK length-prefixed field is truncated.");
            var length = reader.ReadUInt32();
            if (length > int.MaxValue || length >
                reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException(
                    "APK length-prefixed field is invalid.");
            }
            return ReadExactly(reader, checked((int)length));
        }

        private static byte[] ReadExactly(BinaryReader reader, int count)
        {
            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException();
            return bytes;
        }

        private static void ReadExactly(
            Stream stream,
            byte[] buffer,
            int offset,
            int count)
        {
            while (count > 0)
            {
                var read = stream.Read(buffer, offset, count);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
                count -= read;
            }
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] |
                (buffer[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] |
                (buffer[offset + 1] << 8) |
                (buffer[offset + 2] << 16) |
                (buffer[offset + 3] << 24));
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null ||
                left.Length != right.Length)
            {
                return false;
            }
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index]) return false;
            }
            return true;
        }

        private static string ToHex(byte[] value)
        {
            return BitConverter.ToString(value).Replace("-", string.Empty);
        }
    }
}
