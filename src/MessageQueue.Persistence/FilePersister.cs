// -----------------------------------------------------------------------
// <copyright file="FilePersister.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Persistence
{
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
        private readonly PersistenceOptions options;
        private readonly JournalSerializer journalSerializer;
        private readonly SnapshotSerializer snapshotSerializer;
        private readonly string journalPath;
        private readonly string snapshotPath;
        private readonly SemaphoreSlim journalLock;
        private readonly SemaphoreSlim snapshotLock;
        private FileStream journalStream;
        private DateTime lastSnapshotTime;
        private long operationsSinceSnapshot;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of FilePersister.
        /// </summary>
        /// <param name="options">Persistence configuration options.</param>
        public FilePersister(PersistenceOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.journalSerializer = new JournalSerializer();
            this.snapshotSerializer = new SnapshotSerializer();

            // Ensure storage directory exists
            if (!Directory.Exists(this.options.StoragePath))
            {
                Directory.CreateDirectory(this.options.StoragePath);
            }

            this.journalPath = Path.Combine(this.options.StoragePath, this.options.JournalFileName);
            // For snapshots, use a fixed name for the active snapshot (pattern is for archived snapshots)
            this.snapshotPath = Path.Combine(this.options.StoragePath, "snapshot.dat");

            this.journalLock = new SemaphoreSlim(1, 1);
            this.snapshotLock = new SemaphoreSlim(1, 1);
            this.lastSnapshotTime = DateTime.UtcNow;
            this.operationsSinceSnapshot = 0;

            // Open journal file for appending
            this.InitializeJournalStream();
        }

        /// <inheritdoc/>
        public async Task WriteOperationAsync(OperationRecord operation, CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var serialized = await this.journalSerializer.SerializeAsync(operation, cancellationToken);

            await this.journalLock.WaitAsync(cancellationToken);
            try
            {
                if (this.journalStream == null)
                {
                    this.InitializeJournalStream();
                }

                await this.journalStream.WriteAsync(serialized, 0, serialized.Length, cancellationToken);
                await this.journalStream.FlushAsync(cancellationToken);

                Interlocked.Increment(ref this.operationsSinceSnapshot);
            }
            finally
            {
                this.journalLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task CreateSnapshotAsync(QueueSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var serialized = await this.snapshotSerializer.SerializeAsync(snapshot, cancellationToken);

            // Write to temporary file first, then rename for atomicity
            var tempPath = this.snapshotPath + ".tmp";

            await this.snapshotLock.WaitAsync(cancellationToken);
            try
            {
#if NET462
                File.WriteAllBytes(tempPath, serialized);
#else
                await File.WriteAllBytesAsync(tempPath, serialized, cancellationToken);
#endif

                // Atomic rename (overwrite existing snapshot)
                if (File.Exists(this.snapshotPath))
                {
                    File.Delete(this.snapshotPath);
                }

                File.Move(tempPath, this.snapshotPath);

                this.lastSnapshotTime = DateTime.UtcNow;
                Interlocked.Exchange(ref this.operationsSinceSnapshot, 0);
            }
            finally
            {
                this.snapshotLock.Release();

                // Clean up temp file if it still exists
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Best effort
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<QueueSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(this.snapshotPath))
            {
                return null;
            }

            await this.snapshotLock.WaitAsync(cancellationToken);
            try
            {
#if NET462
                var data = File.ReadAllBytes(this.snapshotPath);
#else
                var data = await File.ReadAllBytesAsync(this.snapshotPath, cancellationToken);
#endif

                // Validate header before attempting deserialization
                if (!await this.snapshotSerializer.ValidateHeaderAsync(data, cancellationToken))
                {
                    throw new InvalidDataException("Snapshot file has invalid header.");
                }

                return await this.snapshotSerializer.DeserializeAsync(data, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to load snapshot: {ex.Message}", ex);
            }
            finally
            {
                this.snapshotLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<OperationRecord>> ReplayJournalAsync(long sinceVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(this.journalPath))
            {
                return Enumerable.Empty<OperationRecord>();
            }

            var operations = new List<OperationRecord>();

            await this.journalLock.WaitAsync(cancellationToken);
            try
            {
                // Close current write stream temporarily
                if (this.journalStream != null)
                {
                    await this.journalStream.FlushAsync(cancellationToken);
                    this.journalStream.Dispose();
                    this.journalStream = null;
                }

                // Read journal file
                using (var readStream = new FileStream(this.journalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    while (readStream.Position < readStream.Length)
                    {
                        try
                        {
                            // Read record header to determine size
                            var headerBuffer = new byte[16]; // Sequence(8) + Length(4) + CRC(4)
                            var bytesRead = await readStream.ReadAsync(headerBuffer, 0, headerBuffer.Length, cancellationToken);

                            if (bytesRead < headerBuffer.Length)
                            {
                                break; // End of file or corrupted
                            }

                            // Get payload length
                            var payloadLength = BitConverter.ToInt32(headerBuffer, 8);
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
                            {
                                break; // Incomplete record
                            }

                            // Deserialize record
                            var record = await this.journalSerializer.DeserializeAsync(recordBuffer, cancellationToken);

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
                this.InitializeJournalStream();
            }
            finally
            {
                this.journalLock.Release();
            }

            return operations;
        }

        /// <inheritdoc/>
        public async Task TruncateJournalAsync(long beforeVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(this.journalPath))
            {
                return;
            }

            var tempJournalPath = this.journalPath + ".tmp";

            await this.journalLock.WaitAsync(cancellationToken);
            try
            {
                // Close current write stream
                if (this.journalStream != null)
                {
                    await this.journalStream.FlushAsync(cancellationToken);
                    this.journalStream.Dispose();
                    this.journalStream = null;
                }

                // Read and filter journal entries
                var keptOperations = new List<OperationRecord>();

                using (var readStream = new FileStream(this.journalPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    while (readStream.Position < readStream.Length)
                    {
                        try
                        {
                            // Read record header
                            var headerBuffer = new byte[16];
                            var bytesRead = await readStream.ReadAsync(headerBuffer, 0, headerBuffer.Length, cancellationToken);

                            if (bytesRead < headerBuffer.Length)
                            {
                                break;
                            }

                            var payloadLength = BitConverter.ToInt32(headerBuffer, 8);
                            if (payloadLength < 0 || payloadLength > 10_000_000)
                            {
                                break;
                            }

                            // Read full record
                            var recordBuffer = new byte[16 + payloadLength];
                            Array.Copy(headerBuffer, recordBuffer, 16);
                            bytesRead = await readStream.ReadAsync(recordBuffer, 16, payloadLength, cancellationToken);

                            if (bytesRead < payloadLength)
                            {
                                break;
                            }

                            var record = await this.journalSerializer.DeserializeAsync(recordBuffer, cancellationToken);

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
                        var serialized = await this.journalSerializer.SerializeAsync(op, cancellationToken);
                        await writeStream.WriteAsync(serialized, 0, serialized.Length, cancellationToken);
                    }

                    await writeStream.FlushAsync(cancellationToken);
                }

                // Replace old journal with new one
                File.Delete(this.journalPath);
                File.Move(tempJournalPath, this.journalPath);

                // Reopen journal for writing
                this.InitializeJournalStream();
            }
            finally
            {
                this.journalLock.Release();

                // Clean up temp file if it still exists
                if (File.Exists(tempJournalPath))
                {
                    try
                    {
                        File.Delete(tempJournalPath);
                    }
                    catch
                    {
                        // Best effort
                    }
                }
            }
        }

        /// <inheritdoc/>
        public bool ShouldSnapshot()
        {
            // Time-based trigger
            if ((DateTime.UtcNow - this.lastSnapshotTime) >= this.options.SnapshotInterval)
            {
                return true;
            }

            // Threshold-based trigger
            if (this.operationsSinceSnapshot >= this.options.SnapshotThreshold)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.journalStream?.Dispose();
            this.journalLock?.Dispose();
            this.snapshotLock?.Dispose();

            this.disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Initializes or reopens the journal stream for appending.
        /// </summary>
        private void InitializeJournalStream()
        {
            this.journalStream?.Dispose();
            this.journalStream = new FileStream(
                this.journalPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
        }
    }
}
