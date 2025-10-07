namespace MessageQueue.Core.Options;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Persistence-specific configuration options.
/// </summary>
public class PersistenceOptions
{
    /// <summary>
    /// Directory path for persistence files
    /// </summary>
    [Required]
    public string StoragePath { get; set; } = "./queue-data";

    /// <summary>
    /// Journal file name
    /// </summary>
    public string JournalFileName { get; set; } = "queue.journal";

    /// <summary>
    /// Snapshot file name pattern (will append timestamp)
    /// </summary>
    public string SnapshotFileNamePattern { get; set; } = "queue-snapshot-{0:yyyyMMddHHmmss}.json";

    /// <summary>
    /// Use write-through for journal (flush immediately)
    /// </summary>
    public bool UseWriteThrough { get; set; } = true;

    /// <summary>
    /// Buffer size for journal writes
    /// </summary>
    [Range(1024, 1048576)]
    public int JournalBufferSize { get; set; } = 65536; // 64KB

    /// <summary>
    /// Snapshot compression enabled
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// Number of snapshots to retain
    /// </summary>
    [Range(1, 100)]
    public int SnapshotRetentionCount { get; set; } = 5;

    /// <summary>
    /// Enable CRC validation on read
    /// </summary>
    public bool EnableCrcValidation { get; set; } = true;

    /// <summary>
    /// Serialization format
    /// </summary>
    public SerializationFormat SerializationFormat { get; set; } = SerializationFormat.Json;

    /// <summary>
    /// Time interval between automatic snapshots
    /// </summary>
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Number of operations before triggering automatic snapshot
    /// </summary>
    [Range(1, 1000000)]
    public long SnapshotThreshold { get; set; } = 10000;
}

/// <summary>
/// Serialization format for persistence
/// </summary>
public enum SerializationFormat
{
    /// <summary>
    /// JSON serialization (human-readable, slower)
    /// </summary>
    Json,

    /// <summary>
    /// Binary serialization (faster, compact)
    /// </summary>
    Binary,

    /// <summary>
    /// MessagePack serialization (fast, compact)
    /// </summary>
    MessagePack
}
