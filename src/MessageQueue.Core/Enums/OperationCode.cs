namespace MessageQueue.Core.Enums;

/// <summary>
/// Operation codes for journal (write-ahead log) entries.
/// </summary>
public enum OperationCode
{
    /// <summary>
    /// Enqueue operation - new message added
    /// </summary>
    Enqueue = 1,

    /// <summary>
    /// Replace operation - message replaced due to deduplication
    /// </summary>
    Replace = 2,

    /// <summary>
    /// Checkout operation - message transitioned to InFlight
    /// </summary>
    Checkout = 3,

    /// <summary>
    /// Acknowledge operation - message completed successfully
    /// </summary>
    Acknowledge = 4,

    /// <summary>
    /// Fail operation - message failed and will be retried
    /// </summary>
    Fail = 5,

    /// <summary>
    /// DeadLetter operation - message moved to DLQ
    /// </summary>
    DeadLetter = 6,

    /// <summary>
    /// LeaseRenew operation - lease extended for long-running handler
    /// </summary>
    LeaseRenew = 7,

    /// <summary>
    /// Requeue operation - message returned to Ready state
    /// </summary>
    Requeue = 8,

    /// <summary>
    /// DeadLetterReplay operation - message replayed from DLQ to main queue
    /// </summary>
    DeadLetterReplay = 9,

    /// <summary>
    /// DeadLetterPurge operation - messages purged from DLQ
    /// </summary>
    DeadLetterPurge = 10
}
