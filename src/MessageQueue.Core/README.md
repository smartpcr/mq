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

None - this is the base library

## Usage

Referenced by all other projects in the solution to ensure consistent contracts.

## Phase

**Phase 1** - Foundations & Contracts (Week 1, Days 1-7)
