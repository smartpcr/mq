// -----------------------------------------------------------------------
// <copyright file="RecoveryService.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using MessageQueue.Core;
    using MessageQueue.Core.Enums;
    using MessageQueue.Core.Interfaces;
    using MessageQueue.Core.Models;

    /// <summary>
    /// Service responsible for recovering queue state from snapshots and journal on startup.
    /// </summary>
    public class RecoveryService
    {
        private readonly IPersister persister;
        private readonly ICircularBuffer buffer;
        private readonly DeduplicationIndex deduplicationIndex;
        private readonly IQueueManager? queueManager;

        /// <summary>
        /// Initializes a new instance of RecoveryService.
        /// </summary>
        /// <param name="persister">The persister instance.</param>
        /// <param name="buffer">The circular buffer to restore.</param>
        /// <param name="deduplicationIndex">The deduplication index to restore.</param>
        /// <param name="queueManager">Optional queue manager for setting sequence numbers after recovery.</param>
        public RecoveryService(
            IPersister persister,
            ICircularBuffer buffer,
            DeduplicationIndex deduplicationIndex,
            IQueueManager? queueManager = null)
        {
            this.persister = persister ?? throw new ArgumentNullException(nameof(persister));
            this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            this.deduplicationIndex = deduplicationIndex ?? throw new ArgumentNullException(nameof(deduplicationIndex));
            this.queueManager = queueManager;
        }

        /// <summary>
        /// Recovers queue state from snapshot and journal.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Recovery statistics.</returns>
        public async Task<RecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
        {
            var result = new RecoveryResult
            {
                StartTime = DateTime.UtcNow
            };

            try
            {
                // Step 1: Load latest snapshot
                var snapshot = await this.persister.LoadSnapshotAsync(cancellationToken);

                if (snapshot != null)
                {
                    result.SnapshotVersion = snapshot.Version;
                    result.SnapshotLoaded = true;

                    // Restore messages to buffer, preserving their original state
                    foreach (var message in snapshot.Messages)
                    {
                        await this.buffer.RestoreAsync(message, cancellationToken);
                        result.MessagesRestored++;
                    }

                    // Restore deduplication index
                    foreach (var kvp in snapshot.DeduplicationIndex)
                    {
                        await this.deduplicationIndex.TryAddAsync(kvp.Key, kvp.Value, cancellationToken);
                        result.DeduplicationEntriesRestored++;
                    }

                    result.LastVersion = snapshot.Version;
                }

                // Step 2: Replay journal operations since snapshot
                var journalOperations = await this.persister.ReplayJournalAsync(
                    result.LastVersion,
                    cancellationToken);

                var operations = journalOperations.ToList();
                result.JournalOperationsReplayed = operations.Count;

                foreach (var operation in operations.OrderBy(o => o.SequenceNumber))
                {
                    await this.ApplyOperationAsync(operation, cancellationToken);
                    result.LastVersion = Math.Max(result.LastVersion, operation.SequenceNumber);
                }

                // Rehydrate the sequence number in QueueManager to continue from where we left off
                if (this.queueManager != null && result.LastVersion > 0)
                {
                    this.queueManager.SetSequenceNumber(result.LastVersion);
                }

                // Prune stale deduplication index entries after replay
                await this.PruneStaleDeduplicationEntriesAsync(cancellationToken);

                result.Success = true;
                result.EndTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.EndTime = DateTime.UtcNow;
                throw;
            }

            return result;
        }

        /// <summary>
        /// Applies a journal operation to the queue state.
        /// </summary>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task ApplyOperationAsync(OperationRecord operation, CancellationToken cancellationToken)
        {
            switch (operation.OperationCode)
            {
                case OperationCode.Enqueue:
                    // Deserialize the full envelope from the payload and restore it
                    if (!string.IsNullOrEmpty(operation.Payload))
                    {
                        try
                        {
                            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(operation.Payload);
                            if (envelope != null)
                            {
                                // Use RestoreAsync to preserve the original state
                                await this.buffer.RestoreAsync(envelope, cancellationToken);
                            }
                        }
                        catch (JsonException)
                        {
                            // Skip malformed operations during recovery
                        }
                    }
                    break;

                case OperationCode.Acknowledge:
                    // Remove acknowledged message from buffer
                    await this.buffer.AcknowledgeAsync(operation.MessageId, cancellationToken);
                    break;

                case OperationCode.Requeue:
                    // Requeue operation - the message should already be in buffer
                    // This is handled by the buffer's RequeueAsync
                    await this.buffer.RequeueAsync(operation.MessageId, cancellationToken);
                    break;

                case OperationCode.Replace:
                    // Replace operation for deduplication
                    // Deserialize the replacement envelope and apply the supersede logic
                    if (!string.IsNullOrEmpty(operation.Payload))
                    {
                        try
                        {
                            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(operation.Payload);
                            if (envelope != null && !string.IsNullOrEmpty(envelope.DeduplicationKey))
                            {
                                // Mark old message as superseded and enqueue new one
                                await this.buffer.ReplaceAsync(envelope, envelope.DeduplicationKey, cancellationToken);
                            }
                        }
                        catch (JsonException)
                        {
                            // Skip malformed operations during recovery
                        }
                    }
                    break;

                case OperationCode.Checkout:
                    // Checkout operations don't need to be replayed during recovery
                    // Messages will return to Ready state naturally
                    break;

                case OperationCode.LeaseRenew:
                    // Lease renewals don't need to be replayed
                    // Expired leases will be detected and messages requeued
                    break;

                case OperationCode.DeadLetter:
                    // Dead letter operations - message should be removed from buffer
                    await this.buffer.RemoveAsync(operation.MessageId, cancellationToken);
                    break;

                default:
                    // Unknown operation code, skip
                    break;
            }
        }

        /// <summary>
        /// Detects and recovers from lease expiry during recovery.
        /// Messages that were in-flight during crash are returned to ready state.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of messages recovered from expired leases.</returns>
        public async Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken = default)
        {
            var messages = await this.buffer.GetAllMessagesAsync(cancellationToken);
            int recovered = 0;

            foreach (var message in messages)
            {
                // Check if message is in-flight with expired lease
                if (message.Status == MessageStatus.InFlight &&
                    message.Lease != null &&
                    message.Lease.LeaseExpiry < DateTime.UtcNow)
                {
                    // Requeue the message
                    await this.buffer.RequeueAsync(message.MessageId, cancellationToken);
                    recovered++;
                }
            }

            return recovered;
        }

        /// <summary>
        /// Prunes stale entries from the deduplication index.
        /// Removes entries that point to messages no longer in the buffer or that have been completed/superseded.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task PruneStaleDeduplicationEntriesAsync(CancellationToken cancellationToken = default)
        {
            // Get all current messages in the buffer
            var messages = await this.buffer.GetAllMessagesAsync(cancellationToken);

            // Build a set of valid message IDs and their deduplication keys
            var validMessageIds = new HashSet<Guid>();
            var validDedupKeys = new HashSet<string>();

            foreach (var message in messages)
            {
                // Only keep entries for Ready and InFlight messages
                if (message.Status == MessageStatus.Ready || message.Status == MessageStatus.InFlight)
                {
                    validMessageIds.Add(message.MessageId);
                    if (!string.IsNullOrEmpty(message.DeduplicationKey))
                    {
                        validDedupKeys.Add(message.DeduplicationKey);
                    }
                }
            }

            // Get current dedup index snapshot
            var dedupSnapshot = await this.deduplicationIndex.GetSnapshotAsync(cancellationToken);

            // Remove entries that don't correspond to valid messages
            foreach (var kvp in dedupSnapshot)
            {
                var key = kvp.Key;
                var messageId = kvp.Value;

                // Remove if message ID doesn't exist in buffer or if the key is not in valid set
                if (!validMessageIds.Contains(messageId) || !validDedupKeys.Contains(key))
                {
                    await this.deduplicationIndex.RemoveAsync(key, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Result of a recovery operation.
    /// </summary>
    public class RecoveryResult
    {
        /// <summary>
        /// When recovery started
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// When recovery ended
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Whether recovery was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if recovery failed
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Whether a snapshot was loaded
        /// </summary>
        public bool SnapshotLoaded { get; set; }

        /// <summary>
        /// Version of the loaded snapshot
        /// </summary>
        public long SnapshotVersion { get; set; }

        /// <summary>
        /// Number of messages restored from snapshot
        /// </summary>
        public int MessagesRestored { get; set; }

        /// <summary>
        /// Number of deduplication entries restored
        /// </summary>
        public int DeduplicationEntriesRestored { get; set; }

        /// <summary>
        /// Number of journal operations replayed
        /// </summary>
        public int JournalOperationsReplayed { get; set; }

        /// <summary>
        /// Last version number after recovery
        /// </summary>
        public long LastVersion { get; set; }

        /// <summary>
        /// this.Duration of recovery
        /// </summary>
        public TimeSpan Duration => this.EndTime - this.StartTime;
    }
}
