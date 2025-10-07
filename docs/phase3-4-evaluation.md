# Phase 3 & 4 Implementation Evaluation

## Phase 3: Persistence & Recovery

### What's Working
- The snapshot serializer writes a structured header with magic number, version, payload length, and CRC, giving us the building blocks for validating persisted images before replaying them.【F:src/MessageQueue.Persistence/Serialization/SnapshotSerializer.cs†L17-L88】
- `FilePersister` implements asynchronous journal appends, snapshot creation, and replay/truncation flows that coordinate access with `SemaphoreSlim` locks, so persistence operations can run without blocking callers indefinitely.【F:src/MessageQueue.Persistence/FilePersister.cs†L41-L205】

### Gaps & Discrepancies
- **Configuration Ignored** – Persistence options expose overridable journal/snapshot file names, compression, and retention knobs, but `FilePersister` hard-codes `journal.dat`/`snapshot.dat` and never references those option fields. This prevents operators from matching the design goal of configurable storage layout and retention policies.【F:src/MessageQueue.Core/Options/PersistenceOptions.cs†L9-L44】【F:src/MessageQueue.Persistence/FilePersister.cs†L41-L63】
- **Snapshot Restore Loses Message State** – Recovery pushes snapshot envelopes back through `ICircularBuffer.EnqueueAsync`, which resets every message to `Ready` status and strips lease information. Any in-flight message, superseded placeholder, or explicit status saved in the snapshot is flattened during restore, so recovery diverges from the pre-crash state.【F:src/MessageQueue.Persistence/RecoveryService.cs†L66-L95】【F:src/MessageQueue.Core/CircularBuffer.cs†L37-L99】
- **Journal Replay Skips Mutating Operations** – `ApplyOperationAsync` explicitly no-ops for `Enqueue` and `Replace`, assuming the snapshot already reflects those changes. If the crash occurs after journaled writes but before the next snapshot, the replacement payloads and dedup index updates are silently dropped, violating the write-ahead logging contract described for Phase 3.【F:src/MessageQueue.Persistence/RecoveryService.cs†L118-L157】
- **Sequence Numbers Not Rehydrated** – `QueueManager` always reinitialises its `_sequenceNumber` counter to zero in the constructor and never synchronises with the recovered snapshot/journal version, so subsequent operations can reuse sequence numbers that have already been persisted. That breaks monotonic versioning and can confuse downstream recovery logic that relies on strictly increasing operation IDs.【F:src/MessageQueue.Core/QueueManager.cs†L17-L44】

### Additional Observations
- `RecoveryService` rebuilds the dedup index by blindly adding keys, but it never removes stale entries that might appear after replays remove or supersede messages. Once journal reapplication is fixed this will need to prune the index to avoid dangling pointers.【F:src/MessageQueue.Persistence/RecoveryService.cs†L73-L141】
- Lease recovery only scans the buffer after all restore steps complete; without preserving lease data during enqueue, expired leases are never detected because the data was overwritten earlier.【F:src/MessageQueue.Persistence/RecoveryService.cs†L166-L189】【F:src/MessageQueue.Core/CircularBuffer.cs†L37-L99】

## Phase 4: Handler Dispatcher

- The dispatcher project currently contains only the project file and a README describing the intended registry/worker components—none of the registry, worker loop, or timeout orchestration classes have been implemented yet. Phase 4 remains effectively unstarted from a code perspective.【F:src/MessageQueue.Dispatcher/MessageQueue.Dispatcher.csproj†L1-L17】【F:src/MessageQueue.Dispatcher/README.md†L1-L53】

## Suggested End-to-End Tests

1. **Crash Recovery with Supersede** – Enqueue a message with a dedup key, supersede it with a newer payload, crash after the replace journal entry but before snapshot, then restart. Assert that the newer payload and dedup index mapping are restored. This guards against the current replay no-op for `Replace` operations.【F:src/MessageQueue.Persistence/RecoveryService.cs†L118-L142】
2. **Lease Preservation Across Restart** – Check out a message so it carries lease metadata, persist a snapshot, restart, and verify that recovery keeps the message in-flight (or at least requeues it because the lease expired) rather than resetting it to Ready. This exercises the snapshot restore path that currently overwrites status/lease data.【F:src/MessageQueue.Persistence/RecoveryService.cs†L66-L95】【F:src/MessageQueue.Core/CircularBuffer.cs†L37-L99】
3. **Snapshot Truncation Safety** – Drive the queue to create multiple snapshots in succession, crash mid-way, and confirm on restart that journal truncation never discards operations newer than the snapshot version. This ensures sequencing logic works once `_sequenceNumber` is rehydrated.【F:src/MessageQueue.Persistence/FilePersister.cs†L187-L273】【F:src/MessageQueue.Core/QueueManager.cs†L101-L169】
4. **Dispatcher Happy Path (future Phase 4)** – Once implemented, spin up the dispatcher with a scoped handler, publish a message, and assert that workers respect per-handler parallelism and acknowledge successfully. This will validate the registry + worker orchestration described in the README.【F:src/MessageQueue.Dispatcher/README.md†L9-L28】
