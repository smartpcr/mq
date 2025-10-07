# MessageQueue.Host

## Purpose

Integrated queue host that brings together all components (buffer, persistence, dispatcher, DLQ, admin) with lifecycle management and service registration.

## Contents

- **Services/** - Host implementation
  - `PersistentQueueHost` - Main host service
  - `LifecycleManager` - Startup/shutdown coordination
  - `ServiceRegistration` - DI container setup

- **Configuration/** - Settings binding
  - `ConfigurationBinder` - Options from appsettings.json
  - `Validation` - Configuration validation

## Key Features

- **Unified service registration** - Single `AddMessageQueue()` call
- **Lifecycle management** - Graceful startup/shutdown
- **Configuration binding** - JSON config support
- **Health checks** - Integrated monitoring
- **Logging integration** - Serilog configuration

## Dependencies

All other MessageQueue.* projects

## Usage

```csharp
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Register queue host with all components
        services.AddMessageQueue(context.Configuration.GetSection("MessageQueue"));

        // Register handlers
        services.AddScoped<IMessageHandler<OrderMessage>, OrderHandler>();
        services.Configure<HandlerOptions<OrderMessage>>(opt =>
        {
            opt.MaxParallelism = 4;
            opt.Timeout = TimeSpan.FromMinutes(5);
        });
    })
    .UseSerilog();

var host = builder.Build();
await host.RunAsync();
```

## Configuration (appsettings.json)

```json
{
  "MessageQueue": {
    "Capacity": 10000,
    "PersistencePath": "./queue-data",
    "SnapshotIntervalSeconds": 30,
    "SnapshotThreshold": 1000,
    "DefaultTimeout": "00:02:00",
    "DefaultMaxRetries": 5
  }
}
```

## Phase

**Phase 2-6** - Integration (Throughout all phases)
- Integrates components as they become available
