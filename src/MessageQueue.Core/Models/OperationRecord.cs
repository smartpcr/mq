namespace MessageQueue.Core.Models;

using MessageQueue.Core.Enums;

/// <summary>
/// Journal operation record for write-ahead logging.
/// </summary>
public class OperationRecord
{
    /// <summary>
    /// Operation sequence number (monotonically increasing)
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// Type of operation
    /// </summary>
    public OperationCode OperationCode { get; set; }

    /// <summary>
    /// Message ID affected by this operation
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Serialized operation payload (envelope data)
    /// </summary>
    public string Payload { get; set; } = null!;

    /// <summary>
    /// Timestamp of operation
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// CRC32 checksum for integrity validation
    /// </summary>
    public uint Checksum { get; set; }

    /// <summary>
    /// Optional metadata about the operation
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
