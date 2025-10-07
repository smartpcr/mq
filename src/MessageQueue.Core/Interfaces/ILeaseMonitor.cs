// -----------------------------------------------------------------------
// <copyright file="ILeaseMonitor.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Interfaces;

/// <summary>
/// Monitors message leases and requeues expired messages.
/// Runs as a background service with efficient expiry detection.
/// </summary>
public interface ILeaseMonitor
{
    /// <summary>
    /// Starts the lease monitor background task.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for shutdown</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the lease monitor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for expired leases and requeues messages.
    /// Called periodically by the background task.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CheckExpiredLeasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends a message lease (called by long-running handlers via heartbeat).
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="extensionDuration">Additional lease duration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ExtendLeaseAsync(Guid messageId, TimeSpan extensionDuration, CancellationToken cancellationToken = default);
}
