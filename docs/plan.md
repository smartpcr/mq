# Persistent Circular Queue Implementation Plan

This plan breaks the project into parallel work streams to enable multiple developers to work concurrently. Each phase lists concrete tasks, estimated effort, dependencies, and thorough test coverage (unit and integration) to validate every scenario described in the design.

## Progress Summary

| Phase | Status | Test Results | Completion Date |
|-------|--------|--------------|-----------------|
| Phase 1: Foundations & Contracts | ✅ Complete | 8/8 passing (100%) | Day 7 |
| Phase 2: Core Components | ✅ Complete | 43/43 passing (100%) | Day 20 |
| Phase 3: Persistence & Recovery | ✅ Complete | 63/63 passing (100%) | Day 28 |
| Phase 4: Handler Dispatcher | ✅ Complete | 31/31 passing (100%) | Day 35 |
| Phase 5: Retry & Dead-Letter Logic | ✅ Complete | 30/30 passing (100%) | Day 42 |
| Phase 6: Advanced Features | ✅ Complete | 21/21 passing (100%) | Day 49 |
| Phase 7: Hardening & Release | 🔄 Not Started | - | - |

**Overall Progress**: 6/7 phases complete (86%)
**Total Tests Passing**: 196/196 (100%)

## Work Stream Organization

The project is organized into **4 parallel work streams** that can be developed independently:

1. **Core Stream**: Circular buffer, deduplication, and queue manager
2. **Persistence Stream**: Journal, snapshot, and recovery mechanisms
3. **Dispatcher Stream**: Handler registration, worker loops, and execution
4. **Infrastructure Stream**: Monitoring, admin APIs, and tooling

## Phase 1: Foundations & Contracts (Week 1) ✅ COMPLETED

**Timeline**: Days 1-7
**Parallel Work**: All streams can start simultaneously
**Status**: ✅ Complete (Day 7)
**Test Results**: 8/8 tests passing (100%)

### Stream: Contracts & Models (All developers - Days 1-3)
- [x] Define core interfaces (`IQueueManager`, `ICircularBuffer`, `IPersister`, `IDeadLetterQueue`, `IHandlerDispatcher`, `ILeaseMonitor`, `IMessageHandler<T>`, `IQueuePublisher`). _(Team effort, 2d)_
- [x] Establish shared models (`MessageEnvelope`, `DeadLetterEnvelope`, enums, options records) with serialization attributes. _(Developer A, 2d)_
- [x] Implement dependency injection registrations and configuration binding skeletons. _(Developer B, 2d)_
- [x] Create test infrastructure and mocking utilities. _(Developer C, 2d)_

### Tests (Days 4-7) ✅
- **Unit** _(Developer D)_
  - ✅ Contract validations ensuring option defaults align with design (timeouts, retry counts, buffer capacities).
  - ✅ Serialization round-trip tests for `MessageEnvelope` and `DeadLetterEnvelope` across polymorphic payloads.
  - ✅ DI registration tests confirming service lifetimes and configuration bindings.
- **Integration** _(Developer E)_
  - ✅ Smoke test wiring a minimal host with fake handlers verifying DI resolution and configuration loading.
  - ✅ Serialization compatibility test persisting sample envelopes to file and reading back via the shared models.

**Dependencies**: None
**Deliverable**: ✅ Contracts package, shared models library, test harness
**Files Created**: 23 implementation files + 9 test files

---

## Phase 2: Core Components (Weeks 2-3) ✅ COMPLETED

**Timeline**: Days 8-20
**Parallel Work**: 3 streams running concurrently
**Status**: ✅ Complete (Day 20)
**Test Results**: 43/43 tests passing (100%)

### Stream A: Circular Buffer (Days 8-15) ✅
- [x] Implement lock-free slot array with CAS-based head/tail advancement. _(Developer A1, 3d)_
- [x] Build slot state machine (Empty → Ready → InFlight → Completed/DeadLetter). _(Developer A1, 2d)_
- [x] Implement enqueue, checkout, acknowledge operations. _(Developer A2, 3d)_
- [x] Add buffer metadata tracking (version tokens, capacity management). _(Developer A2, 2d)_

**Tests** _(Developer A3, concurrent)_ ✅
- **Unit**: ✅ CAS advancement, state transitions, boundary conditions (11 tests)
- **Integration**: ✅ Multi-threaded stress test with 50 concurrent producers

### Stream B: Deduplication Index (Days 8-13) ✅
- [x] Implement hash-based key-to-slot index with ConcurrentDictionary. _(Developer B1, 3d)_
- [x] Add optimistic concurrency control for index updates. _(Developer B1, 2d)_
- [x] Implement supersede semantics for in-flight message replacement. _(Developer B2, 3d)_

**Tests** _(Developer B3, concurrent)_ ✅
- **Unit**: ✅ Hash operations, concurrent updates, supersede flags (13 tests)
- **Integration**: ✅ Concurrent dedup scenario with message replacement during processing

### Stream C: Persistence Foundation (Days 8-16) ✅
- [x] Design journal record format with operation codes and CRC checksums. _(Developer C1, 2d)_
- [x] Implement journal serializer with CRC32 validation. _(Developer C1, 3d)_
- [x] Build record reader with header parsing. _(Developer C2, 2d)_
- [x] Create snapshot format for buffer state serialization. _(Developer C2, 3d)_

**Tests** _(Developer C3, concurrent)_ ✅
- **Unit**: ✅ Record encoding/decoding, CRC validation (included in serialization tests)
- **Integration**: ✅ Serialization round-trip validation

### Stream D: Queue Manager Integration (Days 16-20) ✅
- [x] Integrate circular buffer with deduplication index. _(Developer D1, 2d)_
- [x] Implement `QueueManager` coordinating enqueue/checkout/ack operations. _(Developer D1, 3d)_
- [x] Add lease extension and metrics APIs. _(Developer D1, 1d)_

**Tests** _(Developer D2, concurrent)_ ✅
- **Integration**: ✅ End-to-end enqueue-dedup-checkout flow (11 tests)

**Dependencies**: Phase 1 complete
**Deliverable**: ✅ Working in-memory queue with deduplication
**Files Created**: 4 implementation files + 3 test files
**Implementation**: CircularBuffer.cs, DeduplicationIndex.cs, QueueManager.cs, JournalSerializer.cs

---

## Phase 3: Persistence & Recovery (Weeks 3-4) ✅ COMPLETED

**Timeline**: Days 15-28
**Parallel Work**: 2 streams
**Status**: ✅ Complete (Day 28)
**Test Results**: 63/63 tests passing (100%)

### Stream A: Persistence Engine (Days 15-23) ✅
- [x] Implement snapshot serializer for buffer, dedup index, and metadata. _(Developer A1, 4d)_
- [x] Build snapshot deserializer with validation. _(Developer A2, 3d)_
- [x] Integrate persistence triggers (time/threshold/shutdown). _(Developer A3, 3d)_

**Tests** _(Developer A4, concurrent)_ ✅
- **Unit**: ✅ Serialization correctness (20 tests), trigger logic (ShouldSnapshot tests)
- **Integration**: ✅ Graceful shutdown with snapshot verification

### Stream B: Recovery System (Days 18-28) ✅
- [x] Implement startup recovery sequence (snapshot load → journal replay). _(Developer B1, 4d)_
- [x] Add journal truncation after successful snapshot. _(Developer B1, 2d)_
- [x] Implement recovery service with full state restoration. _(Developer B2, 3d)_

**Tests** _(Developer B3, concurrent)_ ✅
- **Unit**: ✅ FilePersister operations (19 tests), recovery logic
- **Integration**: ✅ Crash-recovery scenarios (7 tests), journal replay, deduplication restoration

**Dependencies**: Phase 2 Stream C complete
**Deliverable**: ✅ Persistent queue with crash recovery
**Files Created**: 3 implementation files + 3 test files
**Implementation**: SnapshotSerializer.cs, FilePersister.cs, RecoveryService.cs

---

## Phase 4: Handler Dispatcher (Weeks 4-5) ✅ COMPLETED

**Timeline**: Days 22-35
**Parallel Work**: 2 streams
**Status**: ✅ Complete (Day 35)
**Test Results**: 31/31 tests passing (100%)

### Stream A: Handler Registry & DI (Days 22-28) ✅
- [x] Implement handler registry with type-based lookup. _(Developer A1, 3d)_
- [x] Build DI scope factory integration. _(Developer A1, 2d)_
- [x] Add handler configuration (timeout, parallelism, retry limits). _(Developer A2, 3d)_

**Tests** _(Developer A3, concurrent)_ ✅
- **Unit**: ✅ Registry lookup (16 tests), configuration overrides, DI resolution

### Stream B: Worker Infrastructure (Days 25-35) ✅
- [x] Implement channel-based signaling for ready messages. _(Developer B1, 3d)_
- [x] Build worker loop with message checkout and handler invocation. _(Developer B2, 4d)_
- [x] Add parallelism control (min/max workers per handler type). _(Developer B2, 2d)_
- [x] Implement cancellation token orchestration and timeout enforcement. _(Developer B3, 3d)_

**Tests** _(Developer B4, concurrent)_ ✅
- **Unit**: ✅ Channel signaling, timeout cancellation (16 tests)
- **Integration**: ✅ End-to-end dispatch flow, parallelism enforcement, timeout scenarios (5 tests)

**Dependencies**: Phase 1 complete
**Deliverable**: ✅ Working handler dispatcher with DI integration
**Files Created**: 2 implementation files + 3 test files
**Implementation**: HandlerRegistry.cs, HandlerDispatcher.cs

---

## Phase 5: Retry & Dead-Letter Logic (Weeks 5-6) ✅ COMPLETED

**Timeline**: Days 29-42
**Parallel Work**: 3 streams
**Status**: ✅ Complete (Day 42)
**Test Results**: 30/30 tests passing (100%)

### Stream A: Retry Logic (Days 29-35) ✅
- [x] Implement retry counter updates in queue manager. _(Developer A1, 2d)_
- [x] Add backoff policy configuration and enforcement. _(Developer A1, 2d)_
- [x] Integrate max retry checks with DLQ routing. _(Developer A2, 3d)_

**Tests** _(Developer A3, concurrent)_ ✅
- **Unit**: ✅ Retry counter logic included in QueueManager tests
- **Integration**: ✅ Multi-retry failure flow ending in DLQ (8 tests)

### Stream B: Lease Monitor (Days 32-40) ✅
- [x] Build background lease monitor with dynamic check intervals. _(Developer B1, 4d)_
- [x] Implement lease expiry detection and message requeue. _(Developer B1, 2d)_
- [x] Add lease metadata tracking and extension. _(Developer B2, 3d)_

**Tests** _(Developer B3, concurrent)_ ✅
- **Unit**: ✅ Expiry detection, lease extension (8 tests)
- **Integration**: ✅ Lease expiry requeue scenario, monitor loop tests

### Stream C: Dead-Letter Queue (Days 29-38) ✅
- [x] Implement DLQ with failure metadata storage. _(Developer C1, 4d)_
- [x] Add DLQ persistence hooks. _(Developer C2, 3d)_
- [x] Build management interface for DLQ inspection/replay/purge. _(Developer C2, 3d)_

**Tests** _(Developer C3, concurrent)_ ✅
- **Unit**: ✅ DLQ operations, metadata enrichment (14 tests)
- **Integration**: ✅ DLQ persistence cycle, management operations (8 tests)

**Dependencies**: Phase 2 (buffer), Phase 4 (dispatcher) complete
**Deliverable**: ✅ Full retry/DLQ pipeline
**Files Created**: 2 implementation files + 3 test files + QueueManager updates
**Implementation**: DeadLetterQueue.cs, LeaseMonitor.cs, updated QueueManager.cs

---

## Phase 6: Advanced Features (Weeks 6-7) ✅ COMPLETED

**Timeline**: Days 36-49
**Parallel Work**: 3 streams
**Status**: ✅ Complete (Day 49)
**Test Results**: 21/21 tests passing (100%)

### Stream A: Handler Chaining (Days 36-42) ✅
- [x] Implement `IQueuePublisher` for handler-initiated enqueue. _(Developer A1, 3d)_
- [x] Add correlation ID propagation. _(Developer A1, 2d)_
- [x] Build chaining test scenarios. _(Developer A2, 3d)_

**Tests** _(Developer A2, concurrent)_ ✅
- **Integration**: ✅ Multi-step workflow with correlation tracking (5 tests)

### Stream B: Long-Running Support (Days 39-45) ✅
- [x] Implement lease extension API (already done in Phase 5). _(Developer B1, 3d)_
- [x] Add heartbeat mechanism for progress tracking. _(Developer B1, 2d)_

**Tests** _(Developer B2, concurrent)_ ✅
- **Integration**: ✅ Long-running handler with heartbeat and lease extension (8 tests)

### Stream C: Admin & Monitoring (Days 36-49) ✅
- [x] Build admin API for handler scaling. _(Developer C1, 3d)_
- [x] Implement metrics collection (queue length, latency, throughput). _(Developer C2, 4d)_
- [x] Add manual snapshot/DLQ replay triggers. _(Developer C1, 2d)_
- [ ] Integrate OpenTelemetry exporters. _(Developer C3, 4d)_ (Deferred to Phase 7)

**Tests** _(Developer C4, concurrent)_ ✅
- **Integration**: ✅ Admin operations, metrics accuracy (8 tests)

**Dependencies**: Phase 4, Phase 5 complete
**Deliverable**: ✅ Production-ready feature set
**Files Created**: 5 implementation files + 3 test files
**Implementation**: QueuePublisher.cs, HeartbeatService.cs, QueueAdminApi.cs, IQueueAdminApi.cs, IHeartbeatService.cs

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
