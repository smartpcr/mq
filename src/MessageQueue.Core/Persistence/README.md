# MessageQueue.Persistence

## Purpose

Provides durability and crash recovery through append-only journal and periodic snapshots. Ensures message queue state survives restarts.

## Contents

- **Journal/** - Write-ahead log
  - `JournalWriter` - Append-only operation log with CRC checksums
  - `JournalReader` - Replay journal entries
  - `OperationRecord` - Serialized operations (Enqueue, Replace, Checkout, Ack)

- **Snapshot/** - State serialization
  - `SnapshotWriter` - Serialize buffer, dedup index, metadata to file
  - `SnapshotReader` - Deserialize and restore state
  - `SnapshotFormat` - Binary/JSON format with version header

- **Recovery/** - Startup restoration
  - `RecoveryEngine` - Load snapshot + replay journal
  - `DLQRestorer` - Restore dead-letter queue state

## Key Features

- **Write-Ahead Logging (WAL)** - Atomic operations
- **CRC validation** - Data integrity checks
- **Periodic snapshots** - Time/threshold-based triggers
- **Crash recovery** - Restore from snapshot + journal replay
- **Transactional updates** - Dedup index consistency

## Dependencies

- `MessageQueue.Core` - Interfaces and models

## Configuration

```csharp
services.Configure<PersistenceOptions>(opt =>
{
    opt.PersistencePath = "./queue-data";
    opt.SnapshotIntervalSeconds = 30;
    opt.SnapshotThreshold = 1000;
});
```

## Phase

**Phase 2-3** - Persistence Layer (Weeks 3-4, Days 15-28)
- Stream C: Persistence Foundation (Developers C1, C2, C3)
- Stream B: Recovery System (Developers B1, B2, B3)
