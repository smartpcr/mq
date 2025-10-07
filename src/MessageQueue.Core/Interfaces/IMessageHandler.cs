// -----------------------------------------------------------------------
// <copyright file="IMessageHandler.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Interfaces;

/// <summary>
/// Handler contract for processing messages of type T.
/// Implemented by application code to define message processing logic.
/// </summary>
/// <typeparam name="T">Message type</typeparam>
public interface IMessageHandler<T>
{
    /// <summary>
    /// Processes a message asynchronously.
    /// </summary>
    /// <param name="message">Message to process</param>
    /// <param name="cancellationToken">Cancellation token for timeout enforcement</param>
    /// <returns>Task representing the async operation</returns>
    Task HandleAsync(T message, CancellationToken cancellationToken);
}
