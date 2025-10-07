namespace MessageQueue.Core.Models;

/// <summary>
/// Dead-letter envelope extending MessageEnvelope with failure information.
/// </summary>
public class DeadLetterEnvelope : MessageEnvelope
{
    /// <summary>
    /// Reason for message failure
    /// </summary>
    public string FailureReason { get; set; } = null!;

    /// <summary>
    /// Exception message (if failure was due to exception)
    /// </summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>
    /// Exception stack trace
    /// </summary>
    public string? ExceptionStackTrace { get; set; }

    /// <summary>
    /// Exception type name
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// When the message was moved to DLQ
    /// </summary>
    public DateTime FailureTimestamp { get; set; }

    /// <summary>
    /// Last handler that processed this message
    /// </summary>
    public string? LastHandlerId { get; set; }

    /// <summary>
    /// Creates a DeadLetterEnvelope from a MessageEnvelope
    /// </summary>
    public static DeadLetterEnvelope FromMessageEnvelope(MessageEnvelope envelope, string failureReason, Exception exception = null)
    {
        return new DeadLetterEnvelope
        {
            MessageId = envelope.MessageId,
            MessageType = envelope.MessageType,
            Payload = envelope.Payload,
            DeduplicationKey = envelope.DeduplicationKey,
            Status = Enums.MessageStatus.DeadLetter,
            RetryCount = envelope.RetryCount,
            MaxRetries = envelope.MaxRetries,
            Lease = envelope.Lease,
            LastPersistedVersion = envelope.LastPersistedVersion,
            Metadata = envelope.Metadata,
            EnqueuedAt = envelope.EnqueuedAt,
            FailureReason = failureReason,
            ExceptionMessage = exception?.Message,
            ExceptionStackTrace = exception?.StackTrace,
            ExceptionType = exception?.GetType().FullName,
            FailureTimestamp = DateTime.UtcNow,
            LastHandlerId = envelope.Lease?.HandlerId
        };
    }
}
