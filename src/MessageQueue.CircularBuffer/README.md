# MessageQueue.CircularBuffer

## Purpose

Implements a high-performance, lock-free circular buffer with message deduplication support. This is the core data structure that stores messages in memory.

## Contents

- **Implementation/** - Core buffer logic
  - `ConcurrentCircularBuffer` - Lock-free ring buffer with CAS operations
  - `DeduplicationIndex` - Hash-based key-to-slot mapping
  - `SlotStateMachine` - State transitions (Empty → Ready → InFlight → Completed)

- **Metadata/** - Buffer metadata
  - `BufferMetadata` - Head/tail indices, capacity, version tokens
  - `LeaseMetadata` - Checkout timestamps, expiry tracking

## Key Features

- **Lock-free operations** - CAS-based head/tail advancement
- **Deduplication** - Replace messages with same key (supersede semantics for in-flight)
- **Fixed capacity** - Prevents unbounded memory growth
- **Optimistic concurrency** - Version tokens for conflict detection

## Dependencies

- `MessageQueue.Core` - Interfaces and models

## Usage

```csharp
var buffer = new ConcurrentCircularBuffer(capacity: 10000);
buffer.Enqueue(envelope, deduplicationKey: "order-123");
var message = buffer.Checkout(handlerId: "worker-1", leaseDuration: TimeSpan.FromMinutes(5));
buffer.Acknowledge(message.MessageId);
```

## Phase

**Phase 2** - Core Components (Weeks 2-3, Days 8-20)
- Stream A: Circular Buffer (Developers A1, A2, A3)
- Stream B: Deduplication Index (Developers B1, B2, B3)
