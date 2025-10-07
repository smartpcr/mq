// -----------------------------------------------------------------------
// <copyright file="IHandlerDispatcher.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

/// <summary>
/// Dispatches messages to registered handlers with parallelism control and timeout enforcement.
/// </summary>
public interface IHandlerDispatcher
{
    /// <summary>
    /// Starts the dispatcher and spawns worker tasks for registered handlers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for shutdown</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the dispatcher and gracefully shuts down all workers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that a ready message is available for processing.
    /// Awakens worker loops via channel notification.
    /// </summary>
    /// <param name="messageType">Type of the ready message</param>
    void SignalMessageReady(Type messageType);

    /// <summary>
    /// Dynamically scales handler workers at runtime.
    /// </summary>
    /// <param name="messageType">Handler message type</param>
    /// <param name="instanceCount">Desired number of worker instances</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ScaleHandlerAsync(Type messageType, int instanceCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current handler statistics.
    /// </summary>
    Task<HandlerStatistics> GetStatisticsAsync(Type messageType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics for all registered handlers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary mapping message type name to handler metrics.</returns>
    Task<Dictionary<string, HandlerMetricsSnapshot>> GetMetricsAsync(CancellationToken cancellationToken = default);
}

    /// <summary>
    /// Handler execution statistics
    /// </summary>
    public class HandlerStatistics
    {
        public Type MessageType { get; set; } = null!;
        public int ActiveWorkers { get; set; }
        public int TotalProcessed { get; set; }
        public int TotalFailed { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }
    }
}
