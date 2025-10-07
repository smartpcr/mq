namespace MessageQueue.Persistence;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Models;
using MessageQueue.Core.Options;
using MessageQueue.Persistence.Serialization;

/// <summary>
/// File-based implementation of IPersister using write-ahead logging and snapshots.
/// </summary>
public class FilePersister : IPersister, IDisposable
{
    private readonly PersistenceOptions _options;
    private readonly JournalSerializer _journalSerializer;
    private readonly SnapshotSerializer _snapshotSerializer;
    private readonly string _journalPath;
    private readonly string _snapshotPath;
    private readonly SemaphoreSlim _journalLock;
    private readonly SemaphoreSlim _snapshotLock;
    private FileStream? _journalStream;
    private DateTime _lastSnapshotTime;
    private long _operationsSinceSnapshot;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of FilePersister.
    /// </summary>
    /// <param name="options">Persistence configuration options.</param>
    public FilePersister(PersistenceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _journalSerializer = new JournalSerializer();
        _snapshotSerializer = new SnapshotSerializer();

        // Ensure storage directory exists
        if (!Directory.Exists(_options.StoragePath))
        {
            Directory.CreateDirectory(_options.StoragePath);
        }

        _journalPath = Path.Combine(_options.StoragePath, "journal.dat");
        _snapshotPath = Path.Combine(_options.StoragePath, "snapshot.dat");

        _journalLock = new SemaphoreSlim(1, 1);
        _snapshotLock = new SemaphoreSlim(1, 1);
        _lastSnapshotTime = DateTime.UtcNow;
        _operationsSinceSnapshot = 0;

        // Open journal file for appending
        InitializeJournalStream();
    }

    /// <inheritdoc/>
    public async Task WriteOperationAsync(OperationRecord operation, CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        cancellationToken.ThrowIfCancellationRequested();

        var serialized = await _journalSerializer.SerializeAsync(operation, cancellationToken);

        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            if (_journalStream == null)
            {
                InitializeJournalStream();
            }

            await _journalStream!.WriteAsync(serialized, 0, serialized.Length, cancellationToken);
            await _journalStream.FlushAsync(cancellationToken);

            Interlocked.Increment(ref _operationsSinceSnapshot);
        }
        finally
        {
            _journalLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CreateSnapshotAsync(QueueSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        cancellationToken.ThrowIfCancellationRequested();

        var serialized = await _snapshotSerializer.SerializeAsync(snapshot, cancellationToken);

        // Write to temporary file first, then rename for atomicity
        var tempPath = _snapshotPath + ".tmp";

        await _snapshotLock.WaitAsync(cancellationToken);
        try
        {
#if NET462
            File.WriteAllBytes(tempPath, serialized);
#else
            await File.WriteAllBytesAsync(tempPath, serialized, cancellationToken);
#endif

            // Atomic rename (overwrite existing snapshot)
            if (File.Exists(_snapshotPath))
            {
                File.Delete(_snapshotPath);
            }
            File.Move(tempPath, _snapshotPath);

            _lastSnapshotTime = DateTime.UtcNow;
            Interlocked.Exchange(ref _operationsSinceSnapshot, 0);
        }
        finally
        {
            _snapshotLock.Release();

            // Clean up temp file if it still exists
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Best effort */ }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<QueueSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_snapshotPath))
            return null;

        await _snapshotLock.WaitAsync(cancellationToken);
        try
        {
#if NET462
            var data = File.ReadAllBytes(_snapshotPath);
#else
            var data = await File.ReadAllBytesAsync(_snapshotPath, cancellationToken);
#endif

            // Validate header before attempting deserialization
            if (!await _snapshotSerializer.ValidateHeaderAsync(data, cancellationToken))
            {
                throw new InvalidDataException("Snapshot file has invalid header.");
            }

            return await _snapshotSerializer.DeserializeAsync(data, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to load snapshot: {ex.Message}", ex);
        }
        finally
        {
            _snapshotLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<OperationRecord>> ReplayJournalAsync(long sinceVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_journalPath))
            return Enumerable.Empty<OperationRecord>();

        var operations = new List<OperationRecord>();

        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            // Close current write stream temporarily
            if (_journalStream != null)
            {
                await _journalStream.FlushAsync(cancellationToken);
                _journalStream.Dispose();
                _journalStream = null;
            }

            // Read journal file
            using (var readStream = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                while (readStream.Position < readStream.Length)
                {
                    try
                    {
                        // Read record header to determine size
                        var headerBuffer = new byte[16]; // Sequence(8) + Length(4) + CRC(4)
                        int bytesRead = await readStream.ReadAsync(headerBuffer, 0, headerBuffer.Length, cancellationToken);

                        if (bytesRead < headerBuffer.Length)
                            break; // End of file or corrupted

                        // Get payload length
                        int payloadLength = BitConverter.ToInt32(headerBuffer, 8);
                        if (payloadLength < 0 || payloadLength > 10_000_000) // 10MB sanity check
                        {
                            // Corrupted record, stop replay
                            break;
                        }

                        // Read full record (header + payload)
                        var recordBuffer = new byte[16 + payloadLength];
                        Array.Copy(headerBuffer, recordBuffer, 16);
                        bytesRead = await readStream.ReadAsync(recordBuffer, 16, payloadLength, cancellationToken);

                        if (bytesRead < payloadLength)
                            break; // Incomplete record

                        // Deserialize record
                        var record = await _journalSerializer.DeserializeAsync(recordBuffer, cancellationToken);

                        // Only include operations newer than snapshot
                        if (record.SequenceNumber > sinceVersion)
                        {
                            operations.Add(record);
                        }
                    }
                    catch (InvalidDataException)
                    {
                        // Corrupted record, stop replay
                        break;
                    }
                }
            }

            // Reopen journal for writing
            InitializeJournalStream();
        }
        finally
        {
            _journalLock.Release();
        }

        return operations;
    }

    /// <inheritdoc/>
    public async Task TruncateJournalAsync(long beforeVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_journalPath))
            return;

        var tempJournalPath = _journalPath + ".tmp";

        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            // Close current write stream
            if (_journalStream != null)
            {
                await _journalStream.FlushAsync(cancellationToken);
                _journalStream.Dispose();
                _journalStream = null;
            }

            // Read and filter journal entries
            var keptOperations = new List<OperationRecord>();

            using (var readStream = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                while (readStream.Position < readStream.Length)
                {
                    try
                    {
                        // Read record header
                        var headerBuffer = new byte[16];
                        int bytesRead = await readStream.ReadAsync(headerBuffer, 0, headerBuffer.Length, cancellationToken);

                        if (bytesRead < headerBuffer.Length)
                            break;

                        int payloadLength = BitConverter.ToInt32(headerBuffer, 8);
                        if (payloadLength < 0 || payloadLength > 10_000_000)
                            break;

                        // Read full record
                        var recordBuffer = new byte[16 + payloadLength];
                        Array.Copy(headerBuffer, recordBuffer, 16);
                        bytesRead = await readStream.ReadAsync(recordBuffer, 16, payloadLength, cancellationToken);

                        if (bytesRead < payloadLength)
                            break;

                        var record = await _journalSerializer.DeserializeAsync(recordBuffer, cancellationToken);

                        // Keep operations at or after the truncation point
                        if (record.SequenceNumber >= beforeVersion)
                        {
                            keptOperations.Add(record);
                        }
                    }
                    catch (InvalidDataException)
                    {
                        break;
                    }
                }
            }

            // Write kept operations to new journal
            using (var writeStream = new FileStream(tempJournalPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (var op in keptOperations)
                {
                    var serialized = await _journalSerializer.SerializeAsync(op, cancellationToken);
                    await writeStream.WriteAsync(serialized, 0, serialized.Length, cancellationToken);
                }
                await writeStream.FlushAsync(cancellationToken);
            }

            // Replace old journal with new one
            File.Delete(_journalPath);
            File.Move(tempJournalPath, _journalPath);

            // Reopen journal for writing
            InitializeJournalStream();
        }
        finally
        {
            _journalLock.Release();

            // Clean up temp file if it still exists
            if (File.Exists(tempJournalPath))
            {
                try { File.Delete(tempJournalPath); } catch { /* Best effort */ }
            }
        }
    }

    /// <inheritdoc/>
    public bool ShouldSnapshot()
    {
        // Time-based trigger
        if ((DateTime.UtcNow - _lastSnapshotTime) >= _options.SnapshotInterval)
            return true;

        // Threshold-based trigger
        if (_operationsSinceSnapshot >= _options.SnapshotThreshold)
            return true;

        return false;
    }

    /// <summary>
    /// Initializes or reopens the journal stream for appending.
    /// </summary>
    private void InitializeJournalStream()
    {
        _journalStream?.Dispose();
        _journalStream = new FileStream(
            _journalPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _journalStream?.Dispose();
        _journalLock?.Dispose();
        _snapshotLock?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
