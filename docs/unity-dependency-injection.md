# Unity Container Integration

MessageQueue.Core supports both Microsoft.Extensions.DependencyInjection and Unity container for dependency injection.

## Quick Start with Unity

```csharp
using Unity;
using MessageQueue.Core.DependencyInjection;
using MessageQueue.Core.Interfaces;

// Create Unity container
var container = new UnityContainer();

// Register MessageQueue services
container.AddMessageQueue(options =>
{
    options.Capacity = 10000;
    options.EnablePersistence = true;
    options.PersistencePath = "./queue-data";
    options.TelemetryProvider = TelemetryProvider.OpenTelemetry;
});

// Register message handlers
container.RegisterMessageHandler<OrderMessage, OrderMessageHandler>(options =>
{
    options.MaxParallelism = 5;
    options.Timeout = TimeSpan.FromMinutes(2);
    options.MaxRetries = 3;
});

// Resolve and use services
var publisher = container.Resolve<IQueuePublisher>();
var dispatcher = container.Resolve<IHandlerDispatcher>();

await dispatcher.StartAsync();
await publisher.EnqueueAsync(new OrderMessage { OrderId = 123 });
```

## Extension Methods

### AddMessageQueue

Registers all core MessageQueue services with the Unity container.

```csharp
container.AddMessageQueue(options =>
{
    // Queue configuration
    options.Capacity = 10000;
    options.EnablePersistence = true;
    options.PersistencePath = "./queue-data";

    // Defaults
    options.DefaultTimeout = TimeSpan.FromMinutes(2);
    options.DefaultMaxRetries = 5;

    // Telemetry
    options.TelemetryProvider = TelemetryProvider.OpenTelemetry;
    options.EnableOtlpExport = true;
    options.OtlpEndpoint = "http://localhost:4320";

    // Dead Letter Queue
    options.DeadLetterQueueCapacity = 10000;
});
```

**Services Registered:**
- `IQueueManager` - Queue operations (enqueue, checkout, acknowledge)
- `IQueuePublisher` - Simple enqueue API
- `IDeadLetterQueue` - Dead letter queue management
- `HandlerRegistry` - Handler registration and lookup
- `IHandlerDispatcher` - Message dispatching to handlers
- `IQueueAdminApi` - Administrative operations
- `ILeaseMonitor` - Lease expiration monitoring
- `IHeartbeatService` - Handler heartbeat tracking
- `IPersister` - Persistence layer (if enabled)
- `MessageQueueTelemetry` - Telemetry instrumentation

### RegisterMessageHandler

Registers a message handler with its configuration.

```csharp
// Basic registration
container.RegisterMessageHandler<OrderMessage, OrderMessageHandler>();

// With configuration
container.RegisterMessageHandler<OrderMessage, OrderMessageHandler>(options =>
{
    options.MaxParallelism = 10;
    options.Timeout = TimeSpan.FromMinutes(5);
    options.MaxRetries = 3;
    options.BackoffStrategy = RetryBackoffStrategy.Exponential;
    options.InitialBackoff = TimeSpan.FromSeconds(1);
    options.MaxBackoff = TimeSpan.FromMinutes(5);
});

// With factory function
container.RegisterMessageHandler<OrderMessage>(
    sp => new OrderMessageHandler(sp.GetService<ILogger>()),
    options =>
    {
        options.MaxParallelism = 5;
        options.Timeout = TimeSpan.FromMinutes(2);
    });
```

### BuildServiceProvider

Converts the Unity container to an IServiceProvider for compatibility.

```csharp
var container = new UnityContainer();
container.AddMessageQueue();

// Build IServiceProvider
IServiceProvider serviceProvider = container.BuildServiceProvider();

// Use with IServiceProvider-based code
var publisher = serviceProvider.GetService(typeof(IQueuePublisher)) as IQueuePublisher;
```

## Complete Example

### 1. Define Messages and Handlers

```csharp
// Message types
public class OrderCreatedMessage
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OrderProcessedMessage
{
    public int OrderId { get; set; }
    public bool Success { get; set; }
}

// Handlers
public class OrderCreatedHandler : IMessageHandler<OrderCreatedMessage>
{
    private readonly IOrderService orderService;
    private readonly IQueuePublisher publisher;

    public OrderCreatedHandler(IOrderService orderService, IQueuePublisher publisher)
    {
        this.orderService = orderService;
        this.publisher = publisher;
    }

    public async Task<MessageResult> HandleAsync(
        OrderCreatedMessage message,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // Process order
            await orderService.ProcessOrderAsync(message.OrderId, cancellationToken);

            // Chain to next handler
            await publisher.EnqueueAsync(new OrderProcessedMessage
            {
                OrderId = message.OrderId,
                Success = true
            });

            return MessageResult.Success();
        }
        catch (Exception ex)
        {
            return MessageResult.Retry($"Failed to process order: {ex.Message}");
        }
    }
}

public class OrderProcessedHandler : IMessageHandler<OrderProcessedMessage>
{
    private readonly INotificationService notificationService;

    public OrderProcessedHandler(INotificationService notificationService)
    {
        this.notificationService = notificationService;
    }

    public async Task<MessageResult> HandleAsync(
        OrderProcessedMessage message,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        await notificationService.NotifyOrderProcessedAsync(
            message.OrderId,
            message.Success,
            cancellationToken);

        return MessageResult.Success();
    }
}
```

### 2. Configure Container

```csharp
using Unity;
using MessageQueue.Core.DependencyInjection;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Create and configure Unity container
        var container = new UnityContainer();

        // Register application services
        container.RegisterSingleton<IOrderService, OrderService>();
        container.RegisterSingleton<INotificationService, NotificationService>();

        // Register MessageQueue
        container.AddMessageQueue(options =>
        {
            options.Capacity = 10000;
            options.EnablePersistence = true;
            options.PersistencePath = "./queue-data";
            options.SnapshotIntervalSeconds = 30;
            options.DefaultTimeout = TimeSpan.FromMinutes(2);
            options.DefaultMaxRetries = 3;
            options.TelemetryProvider = TelemetryProvider.OpenTelemetry;
        });

        // Register handlers
        container.RegisterMessageHandler<OrderCreatedMessage, OrderCreatedHandler>(options =>
        {
            options.MaxParallelism = 10;
            options.Timeout = TimeSpan.FromMinutes(5);
            options.MaxRetries = 3;
        });

        container.RegisterMessageHandler<OrderProcessedMessage, OrderProcessedHandler>(options =>
        {
            options.MaxParallelism = 5;
            options.Timeout = TimeSpan.FromSeconds(30);
            options.MaxRetries = 2;
        });

        // Start the queue
        var dispatcher = container.Resolve<IHandlerDispatcher>();
        await dispatcher.StartAsync();

        // Use the queue
        var publisher = container.Resolve<IQueuePublisher>();
        await publisher.EnqueueAsync(new OrderCreatedMessage
        {
            OrderId = 12345,
            Amount = 99.99m,
            CreatedAt = DateTime.UtcNow
        });

        Console.WriteLine("Press any key to stop...");
        Console.ReadKey();

        await dispatcher.StopAsync();
        container.Dispose();
    }
}
```

### 3. Using Admin API

```csharp
var adminApi = container.Resolve<IQueueAdminApi>();

// Get queue statistics
var stats = adminApi.GetQueueStatistics();
Console.WriteLine($"Ready: {stats.ReadyCount}, In-Flight: {stats.InFlightCount}, DLQ: {stats.DlqCount}");

// Get DLQ messages
var dlqMessages = await adminApi.GetDeadLetterMessagesAsync(0, 10);
foreach (var dlqMessage in dlqMessages)
{
    Console.WriteLine($"Failed: {dlqMessage.FailureReason}, Retries: {dlqMessage.RetryCount}");
}

// Replay from DLQ
await adminApi.ReplayFromDeadLetterQueueAsync(messageId, resetRetryCount: true);
```

## Lifetime Management

Unity lifetime managers map to MessageQueue services as follows:

| Service | Unity Lifetime | Scope |
|---------|---------------|-------|
| Core services (QueueManager, etc.) | `ContainerControlledLifetimeManager` | Singleton |
| HandlerRegistry | `ContainerControlledLifetimeManager` | Singleton |
| Message Handlers | `PerResolveLifetimeManager` | Per-resolve (transient) |
| IServiceProvider | `ContainerControlledLifetimeManager` | Singleton |
| IServiceScopeFactory | `ContainerControlledLifetimeManager` | Singleton |

**Handler Scoping**: Each handler invocation gets its own scope via `IServiceScopeFactory.CreateScope()`, allowing scoped dependencies to be resolved per-message.

## Adapter Classes

### UnityServiceProvider

Adapts Unity container to `IServiceProvider`:

```csharp
var container = new UnityContainer();
container.RegisterSingleton<IMyService, MyService>();

var serviceProvider = new UnityServiceProvider(container);
var service = serviceProvider.GetService(typeof(IMyService));
```

### UnityServiceScopeFactory

Provides scoped service resolution:

```csharp
var scopeFactory = new UnityServiceScopeFactory(container);

using (var scope = scopeFactory.CreateScope())
{
    var scopedService = scope.ServiceProvider.GetService(typeof(IMyService));
    // Use scoped service
}
```

## Differences from IServiceCollection

### Registration Syntax

**IServiceCollection:**
```csharp
services.AddMessageQueue(options => { });
services.RegisterMessageHandler<TMessage, THandler>();
```

**Unity:**
```csharp
container.AddMessageQueue(options => { });
container.RegisterMessageHandler<TMessage, THandler>();
```

### Service Resolution

**IServiceCollection:**
```csharp
var serviceProvider = services.BuildServiceProvider();
var service = serviceProvider.GetRequiredService<IQueuePublisher>();
```

**Unity:**
```csharp
var service = container.Resolve<IQueuePublisher>();
// Or via adapter
var serviceProvider = container.BuildServiceProvider();
var service = serviceProvider.GetService(typeof(IQueuePublisher));
```

### Lifetime Management

**IServiceCollection:**
- `AddSingleton` - Singleton lifetime
- `AddScoped` - Scoped lifetime
- `AddTransient` - Transient lifetime

**Unity:**
- `ContainerControlledLifetimeManager` - Singleton lifetime
- `HierarchicalLifetimeManager` - Scoped lifetime (per child container)
- `TransientLifetimeManager` - Transient lifetime
- `PerResolveLifetimeManager` - Per-resolve lifetime

## Best Practices

1. **Dispose the Container**: Always dispose the Unity container when done:
   ```csharp
   using (var container = new UnityContainer())
   {
       container.AddMessageQueue();
       // Use container
   }
   ```

2. **Child Containers for Scopes**: Unity uses child containers for scoping:
   ```csharp
   var scopeFactory = new UnityServiceScopeFactory(container);
   using (var scope = scopeFactory.CreateScope())
   {
       // Scoped resolution
   }
   ```

3. **Avoid Service Locator**: Prefer constructor injection over direct `Resolve()` calls:
   ```csharp
   // Good
   public class OrderService
   {
       private readonly IQueuePublisher publisher;

       public OrderService(IQueuePublisher publisher)
       {
           this.publisher = publisher;
       }
   }

   // Avoid
   public class OrderService
   {
       public void Process(IUnityContainer container)
       {
           var publisher = container.Resolve<IQueuePublisher>();
       }
   }
   ```

4. **Handler Dependencies**: Handlers can inject any registered service:
   ```csharp
   public class MyHandler : IMessageHandler<MyMessage>
   {
       private readonly ILogger logger;
       private readonly IDbContext dbContext;

       public MyHandler(ILogger logger, IDbContext dbContext)
       {
           this.logger = logger;
           this.dbContext = dbContext;
       }
   }
   ```

## Troubleshooting

### ResolutionFailedException

**Problem**: Unity cannot resolve a service.

**Solution**: Ensure the service is registered:
```csharp
container.RegisterType<IMyService, MyService>();
```

### Circular Dependencies

**Problem**: Services have circular dependencies.

**Solution**: Use lazy initialization or redesign dependencies:
```csharp
container.RegisterType<IMyService, MyService>(
    new InjectionFactory(c => new MyService(new Lazy<IOtherService>(() => c.Resolve<IOtherService>()))));
```

### Scope Issues

**Problem**: Scoped services not behaving correctly.

**Solution**: Use child containers for scoping:
```csharp
using (var childContainer = container.CreateChildContainer())
{
    var scopedService = childContainer.Resolve<IScopedService>();
}
```

## See Also

- [Unity Container Documentation](https://github.com/unitycontainer/unity)
- [MessageQueue Core Documentation](./design.md)
- [Handler Configuration](./plan.md)
