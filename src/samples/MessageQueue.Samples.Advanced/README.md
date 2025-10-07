# MessageQueue.Samples.Advanced

## Purpose

Advanced usage examples demonstrating message chaining, DLQ management, long-running handlers, persistence, and admin API operations.

## What's Included

- **Message Chaining** - Multi-step workflows with correlation
- **DLQ Management** - Inspect, replay, purge failed messages
- **Long-Running Handlers** - Lease extension and heartbeat
- **Persistence** - Crash recovery demonstration
- **Admin APIs** - Runtime scaling and metrics
- **Configuration** - Advanced options and tuning

## Running the Sample

```bash
dotnet run --project src/samples/MessageQueue.Samples.Advanced/MessageQueue.Samples.Advanced.csproj
```

## Features Demonstrated

### Message Chaining
```csharp
public class OrderHandler : IMessageHandler<OrderMessage>
{
    private readonly IQueuePublisher _publisher;

    public async Task HandleAsync(OrderMessage msg, CancellationToken ct)
    {
        // Process order
        var result = await ProcessOrderAsync(msg);

        // Chain to payment
        await _publisher.EnqueueAsync(new PaymentMessage
        {
            OrderId = msg.OrderId,
            Amount = result.TotalAmount
        }, deduplicationKey: $"PAY-{msg.OrderId}");
    }
}
```

### DLQ Management
```csharp
var dlq = services.GetRequiredService<IDeadLetterQueueManager>();
var failed = await dlq.GetMessagesAsync(limit: 100);
await dlq.ReplayAsync(messageId);
```

### Long-Running Handler
```csharp
public class LongRunningHandler : IMessageHandler<LongRunningMessage>
{
    private readonly ILeaseExtensionService _lease;

    public async Task HandleAsync(LongRunningMessage msg, CancellationToken ct)
    {
        while (!await IsCompleteAsync(msg.JobId))
        {
            await _lease.ExtendLeaseAsync(msg.MessageId);
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
```

### Runtime Scaling
```csharp
var admin = services.GetRequiredService<IQueueAdminApi>();
await admin.ScaleHandlerAsync<OrderMessage>(instanceCount: 8);
var metrics = await admin.GetMetricsAsync();
```

## Target Audience

Developers implementing production message queue solutions with advanced requirements.
