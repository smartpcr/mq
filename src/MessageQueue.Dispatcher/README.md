# MessageQueue.Dispatcher

## Purpose

Handler execution infrastructure with DI integration, worker management, and lease coordination. Dispatches messages to registered handlers with parallelism control and timeout enforcement.

## Contents

- **Registry/** - Handler management
  - `HandlerRegistry` - Type-based handler lookup
  - `HandlerMetadata` - Timeout, parallelism, retry configuration

- **Workers/** - Execution infrastructure
  - `WorkerPool` - Channel-based worker loops per handler type
  - `MessageDispatcher` - Coordinates checkout and handler invocation
  - `ParallelismController` - Min/max worker enforcement

- **Lease/** - Timeout management
  - `LeaseAcquisition` - Exclusive message checkout
  - `LeaseRenewal` - Heartbeat-based extension
  - `TimeoutEnforcement` - CancellationToken orchestration

## Key Features

- **Channel-based signaling** - No polling, immediate dispatch
- **DI scope management** - Per-message handler resolution
- **Dynamic scaling** - Runtime worker adjustment
- **Timeout enforcement** - Automatic cancellation
- **Lease extension** - Support for long-running handlers

## Dependencies

- `MessageQueue.Core` - Interfaces and models
- `MessageQueue.CircularBuffer` - Buffer integration
- `Microsoft.Extensions.DependencyInjection` - DI resolution

## Usage

```csharp
services.AddScoped<IMessageHandler<OrderMessage>, OrderHandler>();
services.Configure<HandlerOptions<OrderMessage>>(opt =>
{
    opt.MaxParallelism = 4;
    opt.Timeout = TimeSpan.FromMinutes(5);
    opt.MaxRetries = 3;
});
```

## Phase

**Phase 4** - Handler Dispatcher (Weeks 4-5, Days 22-35)
- Stream A: Handler Registry & DI (Developers A1, A2, A3)
- Stream B: Worker Infrastructure (Developers B1, B2, B3, B4)
