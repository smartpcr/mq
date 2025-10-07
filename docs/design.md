# Persistent Circular Queue Design

## 1. Goals and Constraints

The system must provide an in-memory message queue built on top of a circular buffer that periodically persists its content to disk. The queue is shared across multiple concurrent producers and consumers and must guarantee that:

- Queue operations are non-blocking when possible, and contention is minimized through lock-free or fine-grained synchronization.
- Messages are deduplicated based on a caller-provided key; a newer message replaces an older one with the same key.
- Message handlers are resolved through a dependency injection (DI) container and may be spread across multiple assemblies.
- Handlers can be scaled (multiple instances), have configurable execution timeouts, support cancellation, and can enqueue additional messages.
- Failed messages respect retry limits and end up in a dead-letter queue (DLQ).
- Queue state can be restored from persisted storage on restart.
- Consumers should pick up ready messages immediately without relying on periodic polling jobs.
- Long-running handlers can continuously track external operations until completion.

## 2. High-Level Architecture

```
+-----------------------+            +------------------+
| Application Services  |            |  Persistence     |
| (Handlers, Enqueuers) | <--------> |  Store (File)    |
+----------+------------+            +---------+--------+
           |                                  ^
           v                                  |
+----------+------------+            +---------+--------+
| PersistentQueueHost   |            | Dead Letter Queue|
|  - QueueManager       |            |  - Circular Buf  |
|  - HandlerDispatcher  |            |  - File snapshot |
|  - Persister          |            +------------------+
+----------+------------+
           |
           v
   +---------------+
   | CircularBuffer|
   |  - Metadata   |
   |  - Slots      |
   +---------------+
```

### Components

1. **CircularBuffer** – Lock-free (or striped-lock) array of fixed-size slots holding message envelopes. Supports enqueue, replace-by-key, peek, and acknowledgement APIs. Maintains head/tail indices and version tokens for optimistic concurrency.
2. **QueueManager** – Exposes `Enqueue<T>`, `Checkout<T>`, `Acknowledge`, and `Requeue` operations. Coordinates deduplication, retry tracking, DLQ routing, and persistence triggers.
3. **HandlerDispatcher** – Subscribes to buffer events and dispatches messages to registered handlers using the DI container. Manages handler execution, cancellation, timeout, and scaling.
4. **Persister** – Periodically snapshots buffer state, DLQ, and metadata to a file. On startup, loads snapshots into memory and replays incomplete checkpoints.
5. **DeadLetterQueue** – Secondary circular buffer storing failed messages with metadata explaining failure.
6. **Metadata Store** – Maintains per-message metadata (key, retry count, lease owner, lease expiry, last persisted version, etc.).

## 3. Data Model

### Message Envelope

```
MessageEnvelope
├── MessageId (GUID)
├── MessageType (Type token)
├── Key (string/hash)
├── Payload (binary or JSON serialized)
├── RetryCount (int)
├── MaxRetries (int)
├── Lease
│   ├── HandlerId
│   ├── CheckoutTimestamp
│   └── LeaseExpiry
├── Status (Ready, InFlight, Completed, DeadLetter)
├── LastPersistedVersion
└── Metadata (headers, correlation IDs, etc.)
```

All payloads are serialized (e.g., System.Text.Json) to allow generic type handling. Type token maps to handler registrations in the DI container.

### Dead-Letter Envelope

Extends `MessageEnvelope` with `FailureReason`, `LastException`, and `FailureTimestamp`.

## 4. Core Scenarios

### 4.1 Concurrent Circular Buffer Queue

- Fixed-size ring buffer with capacity sized to expected throughput and backpressure requirements.
- Use a `ConcurrentCircularBuffer` abstraction supporting atomic advancement of head and tail via `Interlocked.CompareExchange`.
- Slot states: `Empty`, `Ready`, `InFlight`, `DeadLetter`.
- Deduplication occurs on enqueue: compute key hash, look up existing slot via hash index (open addressing dictionary mapping key → slot index). Upon collision, replace the existing message if the new message has a more recent timestamp or version.
- When replacing, mark old slot as `Superseded` so consumers skip it and GC removes it during persistence.

### 4.2 Persistence Strategy

- Append-only journal file storing operations (enqueue, replace, checkout, acknowledge, dead-letter). Periodic snapshot flush compacts the state.
- Snapshot triggers:
  - Time-based (e.g., every N seconds).
  - Threshold-based (e.g., M operations since last flush).
  - Graceful shutdown.
- Startup sequence:
  1. Check for latest snapshot and apply to buffer.
  2. Replay journal entries newer than snapshot.
  3. Restore DLQ.
- Use CRC checksums per record to ensure file integrity.

### 4.3 Deduplication Semantics

- Enqueue API requires a deduplication key (hashable). If absent, defaults to message ID (no dedupe).
- `DedupIndex` structure maps keys to slot indices and message versions.
- When a new message arrives with the same key:
  - If the old message is `Ready`, replace it (overwrite payload, reset retry count, update persistence version).
  - If `InFlight`, mark old envelope as `Superseded` but allow current handler to finish; upon checkout timeout or acknowledgement, the superseded flag causes the dispatcher to discard the obsolete message.
- Dedup index updates must be persisted with the envelope to ensure crash recovery correctness.

### 4.4 Checkout, Lease, and Timeout Handling

- `Checkout<T>(handlerId, leaseDuration)` returns the next `Ready` message of type `T` and transitions it to `InFlight` with a lease (expiry timestamp).
- `leaseDuration` defaults to handler-specific timeout. Handler dispatcher automatically renews or cancels based on actual execution time.
- If handler finishes successfully, it calls `Acknowledge(messageId)`. If it fails, dispatcher calls `Fail(messageId, exception)` to increment retry count.
- A background lease monitor scans the buffer for expired leases and moves messages back to `Ready` with incremented retry counts.
- When retry count exceeds `MaxRetries`, message is moved to DLQ.

### 4.5 Handler Registration via DI

- Provide `IMessageHandler<TMessage>` interface.
- Application registers handlers and configuration using `IServiceCollection` or Unity modules:

```csharp
services.AddSingleton<IPersistentQueueHost, PersistentQueueHost>();
services.Configure<HandlerOptions<TMessage>>(opt =>
{
    opt.MaxParallelism = 2;
    opt.Timeout = TimeSpan.FromMinutes(5);
    opt.MaxRetries = 5;
});
services.AddScoped<IMessageHandler<TMessage>, ConcreteHandler>();
```

- `PersistentQueueHost` uses DI to resolve handler instances when dispatching messages. Handler metadata (timeout, parallelism) stored in a registry keyed by message type.

### 4.6 Immediate Dispatch Without Timer Jobs

- Use a semaphore or channel-based signaling. When a message becomes `Ready`, enqueue a token into an `AsyncAutoResetEvent`/`Channel`. The dispatcher awaits this signal and immediately dequeues messages.
- Each handler type has a dedicated worker loop running as a `Task` scheduled on thread pool. Workers block on the channel (no busy-wait) and awaken instantly when new messages arrive.
- Scaling is managed by spawning `MaxParallelism` worker tasks per handler type.

### 4.7 Long-Running Handler Support

- Handler receives a `CancellationToken` tied to the configured timeout. For long-running operations:
  - Handler may periodically call external services in a while loop until completion.
  - Handler should respect cancellation by checking the token.
  - For jobs exceeding default timeout, configuration sets a higher timeout or `Timeout = Timeout.InfiniteTimeSpan`.
- Optional progress heartbeat API allows handler to extend its lease if the external job is still running.

### 4.8 Handler Chaining

- Handlers can enqueue additional messages via the injected `IQueuePublisher` abstraction.
- Enqueued messages follow the same dedup/persistence pipeline, enabling orchestrated workflows.
- Use correlation IDs to link chained messages and support sagas.

### 4.9 Dead-Letter Processing

- DLQ stored in separate circular buffer persisted to its own file.
- Operators can inspect, replay, or purge DLQ messages via management API.
- DLQ entries include failure metadata to diagnose root cause.

### 4.10 Handler Instance Count Control

- `HandlerOptions` includes `MaxParallelism` (max concurrent instances) and `MinParallelism` (initial workers).
- Dispatcher manages worker lifecycle: on configuration change, it can spin up or tear down worker tasks.
- Workers use dependency scopes (`IServiceScopeFactory`) to resolve scoped dependencies per message invocation.

## 5. Concurrency Model

- **Producers** use `EnqueueAsync` which acquires slot via CAS and writes message data. Lightweight mutex guards dedup index updates.
- **Consumers** are handler-specific worker loops awaiting channel signals. When awakened, they iterate the circular buffer scanning for ready messages of their type.
- `Checkout` ensures exclusivity by marking slot `InFlight` and storing lease metadata atomically.
- Lease monitor uses `Task.Delay` loops and indexes on expiry times to avoid O(n) scans; e.g., maintain min-heap of leases.

## 6. Persistence Format

- Snapshot file format:
  - Header (version, creation timestamp, capacity, number of records).
  - Serialized `MessageEnvelope`s (binary or JSON) including slot index.
  - Dedup index entries.
  - DLQ records.
- Journal file format:
  - Operation code (Enqueue, Replace, Checkout, Ack, Fail, DeadLetter, LeaseRenew).
  - Payload metadata and serialized message.
- On flush, journal is truncated after snapshot ensures durability.

## 7. Startup Sequence

1. Initialize file storage location (configurable path).
2. Load latest snapshot if present; else create empty structures.
3. Replay journal to restore in-flight states, increment retry counts for expired leases.
4. Spawn handler workers per configuration.
5. Signal readiness; producers may start enqueuing.

## 8. Configuration & Monitoring

- Global settings: buffer capacity, persistence cadence, storage paths, default retry count, default timeout.
- Per-handler settings: timeout, lease duration, max retries, max parallelism, dedup mode (strict vs. append), serialization type.
- Expose metrics: queue length, ready/in-flight counts, persistence latency, handler success/failure counts, DLQ size.
- Provide admin API to adjust configurations at runtime (e.g., scale handler count) and trigger manual snapshot.

## 9. Error Handling & Resilience

- Persistence failures: log and retry with exponential backoff; switch to degraded mode if disk unavailable.
- Handler failures: capture exceptions, increment retry count, respect global throttle.
- Lease expiry: messages returned to queue with updated retry count; escalate to DLQ on max retries.
- Ensure dedup index remains consistent via transactional updates (e.g., update index after message persisted or use WAL entry before in-memory change).

## 10. Technology Choices

- Implementation language: C#/.NET for native integration with IServiceCollection/Unity.
- Synchronization: `Interlocked`, `SemaphoreSlim`, `AsyncAutoResetEvent` (custom), `Channel<T>`.
- Serialization: System.Text.Json (custom converters for polymorphic message types).
- File persistence: memory-mapped files or FileStream with `FileOptions.Asynchronous | FileOptions.WriteThrough`.
- Testing: use stress tests with multiple producers/consumers; persistence crash-recovery tests; handler chaining integration tests.

## 11. Open Questions

- Should deduped messages retain history or be entirely replaced? (Assumed replace-only.)
- Should persistence be transactional across queue and DLQ? (Possible requirement.)
- How to handle schema evolution for message payloads? (Version fields and converters.)

## 12. Implementation Status

### Completed Phases (5/7)

#### Phase 1-3: Foundation, Core Components & Persistence ✅
- All core interfaces defined (IQueueManager, ICircularBuffer, IPersister, etc.)
- CircularBuffer with lock-free CAS operations
- DeduplicationIndex with supersede semantics
- QueueManager coordinating all operations
- Persistence layer with journal + snapshots
- Recovery service with startup restoration
- **Tests**: 114/114 passing (100%)

#### Phase 4: Handler Dispatcher & Worker Infrastructure ✅
**Implementation Details**:
- **HandlerRegistry.cs** (src/MessageQueue.Core/HandlerRegistry.cs:1)
  - Type-based handler registration with `RegisterHandler<TMessage, THandler>`
  - DI scope factory integration via IServiceProvider
  - HandlerOptions storage per message type
  - Thread-safe ConcurrentDictionary for registrations

- **HandlerDispatcher.cs** (src/MessageQueue.Core/HandlerDispatcher.cs:1)
  - Channel-based signaling using System.Threading.Channels
  - Worker pools with configurable parallelism per message type
  - Reflection-based generic method invocation for runtime type resolution
  - Timeout enforcement with CancellationTokenSource
  - Graceful shutdown with worker coordination
  - Metrics tracking (messages processed, failures, avg processing time)

**Key Design Decisions**:
- Used reflection instead of dynamic keyword for .NET 4.6.2 compatibility
- Channel.Writer.TryWrite for non-blocking signal propagation
- Separate worker tasks per message type for isolation
- IServiceScope per message for proper DI lifecycle

**Tests**: 31/31 passing (100%)

#### Phase 5: Retry & Dead-Letter Logic ✅
**Implementation Details**:
- **DeadLetterQueue.cs** (src/MessageQueue.Core/DeadLetterQueue.cs:1)
  - ConcurrentQueue-based storage with failure metadata
  - ReplayAsync using reflection to re-enqueue with proper type
  - PurgeAsync with age-based filtering
  - Metrics by message type and failure reason
  - Persistence integration for DLQ operations

- **LeaseMonitor.cs** (src/MessageQueue.Core/LeaseMonitor.cs:1)
  - Background monitoring loop with dynamic check intervals
  - Calculates next check based on nearest lease expiry
  - ExtendLeaseAsync for long-running handlers
  - StartAsync/StopAsync for lifecycle management

- **QueueManager Updates** (src/MessageQueue.Core/QueueManager.cs:187)
  - Integrated DLQ routing in RequeueAsync on max retries
  - GetPendingMessagesAsync returns Ready + InFlight for lease monitoring
  - Reverse mapping (messageId → deduplicationKey) for cleanup

**Key Design Decisions**:
- Dynamic check intervals (1-10 seconds) based on next expiry to optimize CPU
- Separate DLQ from main buffer for independent scaling
- Reflection-based replay to handle polymorphic message types
- Added operation codes: DeadLetterReplay, DeadLetterPurge

**Tests**: 30/30 passing (100%)

### Remaining Phases

#### Phase 6: Advanced Features (In Progress)
- [ ] Handler chaining with IQueuePublisher
- [ ] Correlation ID propagation
- [ ] Admin API for runtime scaling
- [ ] OpenTelemetry integration

#### Phase 7: Hardening & Release
- [ ] Soak testing (24-hour runs)
- [ ] Chaos testing
- [ ] Performance benchmarking
- [ ] Security review
- [ ] Documentation and runbooks

### Next Steps

1. ✅ ~~Define concrete interfaces~~ (Phase 1 complete)
2. ✅ ~~Prototype circular buffer with dedupe and persistence hooks~~ (Phase 2-3 complete)
3. ✅ ~~Implement dispatcher/worker infrastructure using DI scopes~~ (Phase 4 complete)
4. ✅ ~~Build recovery tests to validate startup restoration~~ (Phase 3 complete)
5. ✅ ~~Define administration APIs for DLQ management~~ (Phase 5 complete)
6. Implement handler chaining and correlation tracking (Phase 6)
7. Build admin APIs for runtime scaling (Phase 6)
8. Integration of OpenTelemetry exporters (Phase 6)
9. Comprehensive system testing and hardening (Phase 7)
