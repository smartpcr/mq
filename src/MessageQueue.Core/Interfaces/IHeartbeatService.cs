// -----------------------------------------------------------------------
// <copyright file="IHeartbeatService.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Service for tracking handler progress through heartbeats.
    /// Enables long-running handlers to signal they are still actively processing.
    /// </summary>
    public interface IHeartbeatService
    {
        /// <summary>
        /// Records a heartbeat for a message being processed.
        /// Automatically extends the lease to prevent timeout.
        /// </summary>
        /// <param name="messageId">Message ID being processed.</param>
        /// <param name="progressPercentage">Optional progress percentage (0-100).</param>
        /// <param name="progressMessage">Optional progress status message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task HeartbeatAsync(
            Guid messageId,
            int? progressPercentage = null,
            string progressMessage = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the last heartbeat time for a message.
        /// </summary>
        /// <param name="messageId">Message ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Last heartbeat timestamp or null if no heartbeat recorded.</returns>
        Task<DateTime?> GetLastHeartbeatAsync(Guid messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets progress information for a message.
        /// </summary>
        /// <param name="messageId">Message ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Progress information or null if not available.</returns>
        Task<HeartbeatProgress?> GetProgressAsync(Guid messageId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Progress information from heartbeats.
    /// </summary>
    public class HeartbeatProgress
    {
        /// <summary>
        /// Gets or sets the message ID.
        /// </summary>
        public Guid MessageId { get; set; }

        /// <summary>
        /// Gets or sets the last heartbeat timestamp.
        /// </summary>
        public DateTime LastHeartbeat { get; set; }

        /// <summary>
        /// Gets or sets the progress percentage (0-100).
        /// </summary>
        public int? ProgressPercentage { get; set; }

        /// <summary>
        /// Gets or sets the progress message.
        /// </summary>
        public string? ProgressMessage { get; set; }

        /// <summary>
        /// Gets or sets the number of heartbeats recorded.
        /// </summary>
        public int HeartbeatCount { get; set; }
    }
}
