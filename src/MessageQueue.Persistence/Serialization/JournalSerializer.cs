// -----------------------------------------------------------------------
// <copyright file="JournalSerializer.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Persistence.Serialization;

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MessageQueue.Core.Models;

/// <summary>
/// Serializes and deserializes journal operation records with CRC validation.
/// </summary>
public class JournalSerializer
{
    private const int RecordHeaderSize = 16; // 8 bytes sequence + 4 bytes length + 4 bytes CRC
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes an operation record to a byte array with CRC checksum.
    /// </summary>
    /// <param name="record">The operation record to serialize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Byte array containing the serialized record with header and CRC.</returns>
    public Task<byte[]> SerializeAsync(OperationRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        cancellationToken.ThrowIfCancellationRequested();

        // Serialize the record to JSON
        var json = JsonSerializer.Serialize(record, JsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(json);

        // Calculate CRC32 checksum
        uint crc = CalculateCrc32(payloadBytes);

        // Create buffer with header + payload
        var buffer = new byte[RecordHeaderSize + payloadBytes.Length];

        // Write header: [SequenceNumber(8)] [PayloadLength(4)] [CRC(4)]
        using (var ms = new MemoryStream(buffer))
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(record.SequenceNumber);
            writer.Write(payloadBytes.Length);
            writer.Write(crc);
            writer.Write(payloadBytes);
        }

        return Task.FromResult(buffer);
    }

    /// <summary>
    /// Deserializes an operation record from a byte array with CRC validation.
    /// </summary>
    /// <param name="data">The byte array to deserialize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized operation record.</returns>
    /// <exception cref="InvalidDataException">Thrown when CRC validation fails.</exception>
    public Task<OperationRecord> DeserializeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (data.Length < RecordHeaderSize)
            throw new ArgumentException("Data is too short to contain a valid record.", nameof(data));

        cancellationToken.ThrowIfCancellationRequested();

        using (var ms = new MemoryStream(data))
        using (var reader = new BinaryReader(ms))
        {
            // Read header
            long sequenceNumber = reader.ReadInt64();
            int payloadLength = reader.ReadInt32();
            uint expectedCrc = reader.ReadUInt32();

            // Validate payload length
            if (data.Length < RecordHeaderSize + payloadLength)
                throw new InvalidDataException("Data length does not match payload length in header.");

            // Read payload
            var payloadBytes = reader.ReadBytes(payloadLength);

            // Validate CRC
            uint actualCrc = CalculateCrc32(payloadBytes);
            if (actualCrc != expectedCrc)
                throw new InvalidDataException($"CRC mismatch. Expected: {expectedCrc}, Actual: {actualCrc}");

            // Deserialize JSON payload
            var json = Encoding.UTF8.GetString(payloadBytes);
            var record = JsonSerializer.Deserialize<OperationRecord>(json, JsonOptions);

            if (record == null)
                throw new InvalidDataException("Failed to deserialize operation record.");

            return Task.FromResult(record);
        }
    }

    /// <summary>
    /// Reads the sequence number from a serialized record without full deserialization.
    /// </summary>
    /// <param name="data">The byte array containing the record header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sequence number.</returns>
    public Task<long> ReadSequenceNumberAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (data.Length < 8)
            throw new ArgumentException("Data is too short to contain sequence number.", nameof(data));

        cancellationToken.ThrowIfCancellationRequested();

        long sequenceNumber = BitConverter.ToInt64(data, 0);
        return Task.FromResult(sequenceNumber);
    }

    /// <summary>
    /// Calculates the total size of a serialized record.
    /// </summary>
    /// <param name="data">The byte array containing at least the header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total record size including header and payload.</returns>
    public Task<int> ReadRecordSizeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (data.Length < RecordHeaderSize)
            throw new ArgumentException("Data is too short to contain record header.", nameof(data));

        cancellationToken.ThrowIfCancellationRequested();

        int payloadLength = BitConverter.ToInt32(data, 8); // Offset 8 for sequence number
        int totalSize = RecordHeaderSize + payloadLength;
        return Task.FromResult(totalSize);
    }

    /// <summary>
    /// Calculates CRC32 checksum for the given data.
    /// </summary>
    /// <param name="data">The data to checksum.</param>
    /// <returns>The CRC32 checksum.</returns>
    private static uint CalculateCrc32(byte[] data)
    {
        const uint polynomial = 0xEDB88320;
        uint crc = 0xFFFFFFFF;

        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
        }

        return ~crc;
    }
}
