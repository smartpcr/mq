# MessageQueue.DeadLetter

## Purpose

Dead-letter queue implementation for messages exceeding retry limits. Provides management APIs for inspection, replay, and purging of failed messages.

## Contents

- **Implementation/** - DLQ storage
  - `DeadLetterBuffer` - Circular buffer for failed messages
  - `FailureMetadata` - Exception details, timestamps, retry history

- **Management/** - Admin operations
  - `DLQManager` - Inspect, replay, purge operations
  - `DLQQuery` - Filter and search failed messages

## Key Features

- **Separate circular buffer** - Independent storage from main queue
- **Failure metadata** - Root cause diagnosis (exception, stack trace)
- **Replay capability** - Requeue messages after fixes
- **Persistence** - DLQ state survives restarts
- **Query API** - Filter by type, time, failure reason

## Dependencies

- `MessageQueue.Core` - Interfaces and models
- `MessageQueue.CircularBuffer` - Buffer implementation

## Usage

```csharp
var dlqManager = services.GetRequiredService<IDeadLetterQueueManager>();

// Inspect failed messages
var failed = await dlqManager.GetMessagesAsync(limit: 100);
foreach (var envelope in failed)
{
    Console.WriteLine($"Failed: {envelope.MessageId}, Reason: {envelope.FailureReason}");
}

// Replay after fix
await dlqManager.ReplayAsync(envelope.MessageId);

// Purge old messages
await dlqManager.PurgeAsync(olderThan: TimeSpan.FromDays(7));
```

## Phase

**Phase 5** - Retry & Dead-Letter Logic (Weeks 5-6, Days 29-42)
- Stream C: Dead-Letter Queue (Developers C1, C2, C3)
