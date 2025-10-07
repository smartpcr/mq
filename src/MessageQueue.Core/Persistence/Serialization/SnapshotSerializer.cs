// -----------------------------------------------------------------------
// <copyright file="SnapshotSerializer.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Persistence.Serialization
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using MessageQueue.Core.Models;

    /// <summary>
    /// Serializes and deserializes queue snapshots with validation.
    /// </summary>
    public class SnapshotSerializer
    {
        private const int HeaderSize = 24; // 8 bytes magic + 8 bytes version + 4 bytes length + 4 bytes CRC
        private const long MagicNumber = 0x4D51534E41505348L; // "MQSNAPSH" in hex
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <summary>
        /// Serializes a queue snapshot to a byte array with header and CRC validation.
        /// </summary>
        /// <param name="snapshot">The snapshot to serialize.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Byte array containing the serialized snapshot.</returns>
        public Task<byte[]> SerializeAsync(QueueSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Serialize snapshot to JSON
            var json = JsonSerializer.Serialize(snapshot, SnapshotSerializer.JsonOptions);
            var payloadBytes = Encoding.UTF8.GetBytes(json);

            // Calculate CRC32 checksum
            var crc = SnapshotSerializer.CalculateCrc32(payloadBytes);

            // Create buffer with header + payload
            var buffer = new byte[SnapshotSerializer.HeaderSize + payloadBytes.Length];

            // Write header: [Magic(8)] [Version(8)] [PayloadLength(4)] [CRC(4)]
            using (var ms = new MemoryStream(buffer))
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(SnapshotSerializer.MagicNumber);
                writer.Write(snapshot.Version);
                writer.Write(payloadBytes.Length);
                writer.Write(crc);
                writer.Write(payloadBytes);
            }

            return Task.FromResult(buffer);
        }

        /// <summary>
        /// Deserializes a queue snapshot from a byte array with validation.
        /// </summary>
        /// <param name="data">The byte array to deserialize.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The deserialized snapshot.</returns>
        /// <exception cref="InvalidDataException">Thrown when validation fails.</exception>
        public Task<QueueSnapshot> DeserializeAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length < SnapshotSerializer.HeaderSize)
            {
                throw new InvalidDataException("Data is too short to contain a valid snapshot header.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                // Read and validate magic number
                var magic = reader.ReadInt64();
                if (magic != SnapshotSerializer.MagicNumber)
                {
                    throw new InvalidDataException($"Invalid magic number. Expected: {SnapshotSerializer.MagicNumber:X}, Actual: {magic:X}");
                }

                // Read header fields
                var version = reader.ReadInt64();
                var payloadLength = reader.ReadInt32();
                var expectedCrc = reader.ReadUInt32();

                // Validate payload length
                if (data.Length < SnapshotSerializer.HeaderSize + payloadLength)
                {
                    throw new InvalidDataException("Data length does not match payload length in header.");
                }

                // Read payload
                var payloadBytes = reader.ReadBytes(payloadLength);

                // Validate CRC
                var actualCrc = SnapshotSerializer.CalculateCrc32(payloadBytes);
                if (actualCrc != expectedCrc)
                {
                    throw new InvalidDataException($"CRC mismatch. Expected: {expectedCrc}, Actual: {actualCrc}");
                }

                // Deserialize JSON payload
                var json = Encoding.UTF8.GetString(payloadBytes);
                var snapshot = JsonSerializer.Deserialize<QueueSnapshot>(json, SnapshotSerializer.JsonOptions);

                if (snapshot == null)
                {
                    throw new InvalidDataException("Failed to deserialize snapshot.");
                }

                // Additional validation
                if (snapshot.Version != version)
                {
                    throw new InvalidDataException($"Version mismatch between header ({version}) and payload ({snapshot.Version}).");
                }

                return Task.FromResult(snapshot);
            }
        }

        /// <summary>
        /// Reads the snapshot version from a serialized snapshot without full deserialization.
        /// </summary>
        /// <param name="data">The byte array containing the snapshot header.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The snapshot version.</returns>
        public Task<long> ReadVersionAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length < 16) // Need at least magic + version
            {
                throw new ArgumentException("Data is too short to contain snapshot version.", nameof(data));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Read magic number
            var magic = BitConverter.ToInt64(data, 0);
            if (magic != SnapshotSerializer.MagicNumber)
            {
                throw new InvalidDataException($"Invalid magic number. Expected: {SnapshotSerializer.MagicNumber:X}, Actual: {magic:X}");
            }

            // Read version
            var version = BitConverter.ToInt64(data, 8);
            return Task.FromResult(version);
        }

        /// <summary>
        /// Validates snapshot header without full deserialization.
        /// </summary>
        /// <param name="data">The byte array to validate.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the header is valid.</returns>
        public Task<bool> ValidateHeaderAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null || data.Length < SnapshotSerializer.HeaderSize)
            {
                return Task.FromResult(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var magic = BitConverter.ToInt64(data, 0);
                if (magic != SnapshotSerializer.MagicNumber)
                {
                    return Task.FromResult(false);
                }

                var payloadLength = BitConverter.ToInt32(data, 16);
                if (payloadLength < 0 || payloadLength > 100_000_000) // 100MB sanity check
                {
                    return Task.FromResult(false);
                }

                if (data.Length < SnapshotSerializer.HeaderSize + payloadLength)
                {
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Calculates CRC32 checksum for the given data.
        /// </summary>
        /// <param name="data">The data to checksum.</param>
        /// <returns>The CRC32 checksum.</returns>
        private static uint CalculateCrc32(byte[] data)
        {
            const uint polynomial = 0xEDB88320;
            var crc = 0xFFFFFFFFu;

            foreach (var b in data)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc = (crc >> 1) ^ polynomial;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return ~crc;
        }
    }
}
