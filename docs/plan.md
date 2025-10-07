# Persistent Circular Queue Implementation Plan

This plan breaks the project into parallel work streams to enable multiple developers to work concurrently. Each phase lists concrete tasks, estimated effort, dependencies, and thorough test coverage (unit and integration) to validate every scenario described in the design.

## Work Stream Organization

The project is organized into **4 parallel work streams** that can be developed independently:

1. **Core Stream**: Circular buffer, deduplication, and queue manager
2. **Persistence Stream**: Journal, snapshot, and recovery mechanisms
3. **Dispatcher Stream**: Handler registration, worker loops, and execution
4. **Infrastructure Stream**: Monitoring, admin APIs, and tooling

## Phase 1: Foundations & Contracts (Week 1)

**Timeline**: Days 1-7
**Parallel Work**: All streams can start simultaneously

### Stream: Contracts & Models (All developers - Days 1-3)
- [ ] Define core interfaces (`IQueueManager`, `ICircularBuffer`, `IPersister`, `IDeadLetterQueue`, `IHandlerDispatcher`, `ILeaseMonitor`, `IMessageHandler<T>`, `IQueuePublisher`). _(Team effort, 2d)_
- [ ] Establish shared models (`MessageEnvelope`, `DeadLetterEnvelope`, enums, options records) with serialization attributes. _(Developer A, 2d)_
- [ ] Implement dependency injection registrations and configuration binding skeletons. _(Developer B, 2d)_
- [ ] Create test infrastructure and mocking utilities. _(Developer C, 2d)_

### Tests (Days 4-7)
- **Unit** _(Developer D)_
  - Contract validations ensuring option defaults align with design (timeouts, retry counts, buffer capacities).
  - Serialization round-trip tests for `MessageEnvelope` and `DeadLetterEnvelope` across polymorphic payloads.
  - DI registration tests confirming service lifetimes and configuration bindings.
- **Integration** _(Developer E)_
  - Smoke test wiring a minimal host with fake handlers verifying DI resolution and configuration loading.
  - Serialization compatibility test persisting sample envelopes to file and reading back via the shared models.

**Dependencies**: None
**Deliverable**: Contracts package, shared models library, test harness

---

## Phase 2: Core Components (Weeks 2-3)

**Timeline**: Days 8-20
**Parallel Work**: 3 streams running concurrently

### Stream A: Circular Buffer (Days 8-15)
- [ ] Implement lock-free slot array with CAS-based head/tail advancement. _(Developer A1, 3d)_
- [ ] Build slot state machine (Empty → Ready → InFlight → Completed/DeadLetter). _(Developer A1, 2d)_
- [ ] Implement enqueue, checkout, acknowledge operations. _(Developer A2, 3d)_
- [ ] Add buffer metadata tracking (version tokens, capacity management). _(Developer A2, 2d)_

**Tests** _(Developer A3, concurrent)_
- **Unit**: CAS advancement, state transitions, boundary conditions
- **Integration**: Multi-threaded stress test with 100+ concurrent producers/consumers

### Stream B: Deduplication Index (Days 8-13)
- [ ] Implement hash-based key-to-slot index with open addressing. _(Developer B1, 3d)_
- [ ] Add optimistic concurrency control for index updates. _(Developer B1, 2d)_
- [ ] Implement supersede semantics for in-flight message replacement. _(Developer B2, 3d)_

**Tests** _(Developer B3, concurrent)_
- **Unit**: Hash collision handling, version comparisons, supersede flags
- **Integration**: Concurrent dedup scenario with message replacement during processing

### Stream C: Persistence Foundation (Days 8-16)
- [ ] Design journal record format with operation codes and CRC checksums. _(Developer C1, 2d)_
- [ ] Implement journal writer with append-only semantics. _(Developer C1, 3d)_
- [ ] Build journal reader with CRC validation. _(Developer C2, 2d)_
- [ ] Create snapshot format for buffer state serialization. _(Developer C2, 3d)_

**Tests** _(Developer C3, concurrent)_
- **Unit**: Record encoding/decoding, CRC validation, file I/O error handling
- **Integration**: Write-replay cycle validation

### Stream D: Queue Manager Integration (Days 16-20)
- [ ] Integrate circular buffer with deduplication index. _(Developer D1, 2d)_
- [ ] Implement `QueueManager` coordinating enqueue/checkout/ack operations. _(Developer D1, 3d)_

**Tests** _(Developer D2, concurrent)_
- **Integration**: End-to-end enqueue-dedup-checkout flow

**Dependencies**: Phase 1 complete
**Deliverable**: Working in-memory queue with deduplication

---

## Phase 3: Persistence & Recovery (Weeks 3-4)

**Timeline**: Days 15-28
**Parallel Work**: 2 streams

### Stream A: Persistence Engine (Days 15-23)
- [ ] Implement snapshot serializer for buffer, dedup index, and metadata. _(Developer A1, 4d)_
- [ ] Build snapshot deserializer with validation. _(Developer A2, 3d)_
- [ ] Integrate persistence triggers (time/threshold/shutdown). _(Developer A3, 3d)_

**Tests** _(Developer A4, concurrent)_
- **Unit**: Serialization correctness, trigger logic
- **Integration**: Graceful shutdown with snapshot verification

### Stream B: Recovery System (Days 18-28)
- [ ] Implement startup recovery sequence (snapshot load → journal replay). _(Developer B1, 4d)_
- [ ] Add journal truncation after successful snapshot. _(Developer B1, 2d)_
- [ ] Implement DLQ restoration hooks. _(Developer B2, 3d)_

**Tests** _(Developer B3, concurrent)_
- **Integration**: Crash-recovery scenarios, corrupt file handling, partial replay

**Dependencies**: Phase 2 Stream C complete
**Deliverable**: Persistent queue with crash recovery

---

## Phase 4: Handler Dispatcher (Weeks 4-5)

**Timeline**: Days 22-35
**Parallel Work**: 2 streams

### Stream A: Handler Registry & DI (Days 22-28)
- [ ] Implement handler registry with type-based lookup. _(Developer A1, 3d)_
- [ ] Build DI scope factory integration. _(Developer A1, 2d)_
- [ ] Add handler configuration (timeout, parallelism, retry limits). _(Developer A2, 3d)_

**Tests** _(Developer A3, concurrent)_
- **Unit**: Registry lookup, configuration overrides, DI resolution

### Stream B: Worker Infrastructure (Days 25-35)
- [ ] Implement channel-based signaling for ready messages. _(Developer B1, 3d)_
- [ ] Build worker loop with message checkout and handler invocation. _(Developer B2, 4d)_
- [ ] Add parallelism control (min/max workers per handler type). _(Developer B2, 2d)_
- [ ] Implement cancellation token orchestration and timeout enforcement. _(Developer B3, 3d)_

**Tests** _(Developer B4, concurrent)_
- **Unit**: Channel signaling, timeout cancellation
- **Integration**: End-to-end dispatch flow, parallelism enforcement, timeout scenarios

**Dependencies**: Phase 1 complete
**Deliverable**: Working handler dispatcher with DI integration

---

## Phase 5: Retry & Dead-Letter Logic (Weeks 5-6)

**Timeline**: Days 29-42
**Parallel Work**: 3 streams

### Stream A: Retry Logic (Days 29-35)
- [ ] Implement retry counter updates in queue manager. _(Developer A1, 2d)_
- [ ] Add backoff policy configuration and enforcement. _(Developer A1, 2d)_
- [ ] Integrate max retry checks with DLQ routing. _(Developer A2, 3d)_

**Tests** _(Developer A3, concurrent)_
- **Unit**: Retry counter logic, backoff calculations
- **Integration**: Multi-retry failure flow ending in DLQ

### Stream B: Lease Monitor (Days 32-40)
- [ ] Build background lease monitor with min-heap scheduling. _(Developer B1, 4d)_
- [ ] Implement lease expiry detection and message requeue. _(Developer B1, 2d)_
- [ ] Add lease metadata tracking. _(Developer B2, 3d)_

**Tests** _(Developer B3, concurrent)_
- **Unit**: Heap operations, expiry detection
- **Integration**: Lease expiry requeue scenario

### Stream C: Dead-Letter Queue (Days 29-38)
- [ ] Implement DLQ circular buffer with failure metadata. _(Developer C1, 4d)_
- [ ] Add DLQ persistence hooks. _(Developer C2, 3d)_
- [ ] Build management interface for DLQ inspection/replay/purge. _(Developer C2, 3d)_

**Tests** _(Developer C3, concurrent)_
- **Unit**: DLQ operations, metadata enrichment
- **Integration**: DLQ persistence cycle, management operations

**Dependencies**: Phase 2 (buffer), Phase 4 (dispatcher) complete
**Deliverable**: Full retry/DLQ pipeline

---

## Phase 6: Advanced Features (Weeks 6-7)

**Timeline**: Days 36-49
**Parallel Work**: 3 streams

### Stream A: Handler Chaining (Days 36-42)
- [ ] Implement `IQueuePublisher` for handler-initiated enqueue. _(Developer A1, 3d)_
- [ ] Add correlation ID propagation. _(Developer A1, 2d)_
- [ ] Build chaining test scenarios. _(Developer A2, 3d)_

**Tests** _(Developer A2, concurrent)_
- **Integration**: Multi-step workflow with correlation tracking

### Stream B: Long-Running Support (Days 39-45)
- [ ] Implement lease extension API. _(Developer B1, 3d)_
- [ ] Add heartbeat mechanism for progress tracking. _(Developer B1, 2d)_

**Tests** _(Developer B2, concurrent)_
- **Integration**: Long-running handler with heartbeat and lease extension

### Stream C: Admin & Monitoring (Days 36-49)
- [ ] Build admin API for handler scaling. _(Developer C1, 3d)_
- [ ] Implement metrics collection (queue length, latency, throughput). _(Developer C2, 4d)_
- [ ] Add manual snapshot/DLQ replay triggers. _(Developer C1, 2d)_
- [ ] Integrate OpenTelemetry exporters. _(Developer C3, 4d)_

**Tests** _(Developer C4, concurrent)_
- **Integration**: Admin operations, metrics accuracy, telemetry export

**Dependencies**: Phase 4, Phase 5 complete
**Deliverable**: Production-ready feature set

---

## Phase 7: Hardening & Release (Weeks 7-8)

**Timeline**: Days 43-56
**All hands on deck**

### Quality Assurance (Days 43-50)
- [ ] Full-system soak tests (24-hour run). _(Developer Team A, 3d)_
- [ ] Chaos testing (persistence failure, handler crashes). _(Developer Team B, 3d)_
- [ ] Performance benchmarking and profiling. _(Developer Team C, 3d)_

### Documentation & Release (Days 48-56)
- [ ] Operational runbooks and troubleshooting guides. _(Developer D1, 3d)_
- [ ] API documentation and usage examples. _(Developer D2, 3d)_
- [ ] Security review (file permissions, error handling). _(Developer E1, 3d)_
- [ ] Package publishing and versioning. _(Developer E2, 2d)_

**Deliverable**: Production release v1.0

---

## Gantt Chart

```plantuml
@startgantt
projectscale daily
printscale daily
Project starts 2025-10-06

-- Phase 1: Foundations --
[Contracts & Models] starts 2025-10-06 and lasts 3 days
[DI Registration] starts 2025-10-06 and lasts 2 days
[Test Infrastructure] starts 2025-10-06 and lasts 2 days
[Unit Tests - Phase 1] starts 2025-10-09 and lasts 4 days
[Integration Tests - Phase 1] starts 2025-10-09 and lasts 4 days

-- Phase 2: Core Components --
[Circular Buffer] starts 2025-10-13 and lasts 8 days
[Deduplication Index] starts 2025-10-13 and lasts 6 days
[Persistence Foundation] starts 2025-10-13 and lasts 9 days
[Queue Manager Integration] starts 2025-10-21 and lasts 5 days

-- Phase 3: Persistence --
[Snapshot Engine] starts 2025-10-20 and lasts 9 days
[Recovery System] starts 2025-10-23 and lasts 11 days

-- Phase 4: Dispatcher --
[Handler Registry] starts 2025-10-27 and lasts 7 days
[Worker Infrastructure] starts 2025-10-30 and lasts 11 days

-- Phase 5: Retry & DLQ --
[Retry Logic] starts 2025-11-03 and lasts 7 days
[Lease Monitor] starts 2025-11-06 and lasts 9 days
[Dead-Letter Queue] starts 2025-11-03 and lasts 10 days

-- Phase 6: Advanced Features --
[Handler Chaining] starts 2025-11-10 and lasts 7 days
[Long-Running Support] starts 2025-11-13 and lasts 7 days
[Admin & Monitoring] starts 2025-11-10 and lasts 14 days

-- Phase 7: Hardening --
[Soak Testing] starts 2025-11-17 and lasts 8 days
[Documentation] starts 2025-11-22 and lasts 9 days
[Security Review] starts 2025-11-22 and lasts 9 days

@endgantt
```

## Scenario Coverage Matrix

| Design Scenario | Phase | Unit Test Coverage | Integration Test Coverage |
| --- | --- | --- | --- |
| Concurrent circular buffer operations | Phase 2 | Slot state machine, CAS head/tail tests | Stress harness, dedup replacement flow |
| Deduplication semantics | Phase 2 | Dedup index collision & supersede | Concurrent producer replacement scenario |
| Persistence strategy (journal + snapshot) | Phase 3 | Journal encoding/trigger logic | Crash recovery, graceful shutdown |
| Startup restoration | Phase 3 | Snapshot/journal decoding | Recovery integration test |
| DI handler dispatching | Phase 4 | Registry lookup | Dispatcher end-to-end flow |
| Lease/timeout handling | Phases 4 & 5 | Lease renewal logic | Timeout requeue, lease expiry recovery |
| Retry & DLQ routing | Phase 5 | Retry counters, DLQ operations | Failure-to-DLQ flow, DLQ persistence |
| Handler scaling & signaling | Phase 4 | Channel signaling | Parallelism enforcement |
| Handler chaining | Phase 6 | Publisher invariants | Correlation workflow |
| Long-running support | Phase 6 | Lease extension validation | Heartbeat lease extension |
| Admin & monitoring | Phase 6 & 7 | Metrics aggregation | Admin API operations |
| Error handling & resilience | Phase 7 | Alerting fallback logic | Chaos testing, soak tests |
| Immediate dispatch w/o polling | Phase 4 | Channel logic | Dispatcher flow |
| Persistence failure resilience | Phase 3 & 7 | Trigger fallbacks | Chaos test with persistence outage |

This implementation plan ensures every scenario from the design document is validated by both unit and integration tests while delivering a structured execution roadmap with estimates and timeline.
