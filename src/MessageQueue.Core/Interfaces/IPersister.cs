// -----------------------------------------------------------------------
// <copyright file="IPersister.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Interfaces;

using MessageQueue.Core.Models;

/// <summary>
/// Persistence layer for queue state durability.
/// Manages journal and snapshot operations.
/// </summary>
public interface IPersister
{
    /// <summary>
    /// Writes an operation to the journal (write-ahead log).
    /// </summary>
    /// <param name="operation">Operation record to persist</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task WriteOperationAsync(OperationRecord operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a snapshot of current queue state.
    /// </summary>
    /// <param name="snapshot">Snapshot data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CreateSnapshotAsync(QueueSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the latest snapshot from disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Snapshot or null if none exists</returns>
    Task<QueueSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays journal operations since the last snapshot.
    /// </summary>
    /// <param name="sinceVersion">Version to replay from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of operations to replay</returns>
    Task<IEnumerable<OperationRecord>> ReplayJournalAsync(long sinceVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Truncates the journal after a successful snapshot.
    /// </summary>
    /// <param name="beforeVersion">Truncate operations before this version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task TruncateJournalAsync(long beforeVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if persistence triggers should fire (time-based or threshold-based).
    /// </summary>
    bool ShouldSnapshot();
}
