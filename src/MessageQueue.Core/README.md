# MessageQueue.Core

## Purpose

Foundation library containing all core interfaces, models, enums, and configuration options for the Message Queue system. This project has no dependencies and serves as the contract layer for all other components.

## Contents

- **Interfaces/** - Core abstractions
  - `IQueueManager` - Queue operations (enqueue, checkout, acknowledge)
  - `ICircularBuffer` - Lock-free buffer contract
  - `IPersister` - Persistence layer contract
  - `IHandlerDispatcher` - Handler execution contract
  - `ILeaseMonitor` - Lease management contract
  - `IMessageHandler<T>` - Handler implementation contract
  - `IQueuePublisher` - Message publishing contract
  - `IDeadLetterQueue` - DLQ contract

- **Models/** - Data structures
  - `MessageEnvelope` - Complete message with metadata
  - `DeadLetterEnvelope` - Failed message with failure info
  - `MessageMetadata` - Headers, correlation IDs

- **Enums/** - Status and operation codes
  - `MessageStatus` - Ready, InFlight, Completed, DeadLetter, Superseded
  - `OperationCode` - Enqueue, Replace, Checkout, Ack, Fail, DeadLetter

- **Options/** - Configuration classes
  - `QueueOptions` - Global queue settings
  - `HandlerOptions<T>` - Per-handler configuration

## Dependencies

- Microsoft.Extensions.DependencyInjection.Abstractions (8.0.2) - For DI integration
- System.Threading.Channels (8.0.0) - For channel-based signaling

## Implementation

This project now includes concrete implementations added in later phases:

### Phase 2: Core Components (Days 8-20) ✅
- **CircularBuffer.cs** - Lock-free circular buffer with CAS operations
- **DeduplicationIndex.cs** - Thread-safe deduplication with ConcurrentDictionary
- **QueueManager.cs** - Coordinates enqueue, checkout, acknowledge, requeue operations

### Phase 4: Handler Dispatcher (Days 22-35) ✅
- **HandlerRegistry.cs** - Type-based handler registration with DI integration
- **HandlerDispatcher.cs** - Worker pools with channel-based message dispatch

### Phase 5: Retry & Dead-Letter Logic (Days 29-42) ✅
- **DeadLetterQueue.cs** - Failed message storage with replay/purge capabilities
- **LeaseMonitor.cs** - Background service for lease expiry detection

## Usage

Referenced by all other projects in the solution to ensure consistent contracts.

## Test Coverage

- Phase 1: 8/8 tests (100%)
- Phase 2: 43/43 tests (100%)
- Phase 4: 31/31 tests (100%)
- Phase 5: 30/30 tests (100%)

**Total**: 112/112 tests passing in MessageQueue.Core components
