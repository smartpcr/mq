# MessageQueue API Documentation

## Table of Contents
1. [Quick Start](#quick-start)
2. [Core APIs](#core-apis)
3. [Handler APIs](#handler-apis)
4. [Admin APIs](#admin-apis)
5. [Advanced Features](#advanced-features)
6. [Examples](#examples)

## Quick Start

### Installation

```bash
dotnet add package MessageQueue.Host
```

### Basic Setup

```csharp
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using MessageQueue.Host;
using Microsoft.Extensions.DependencyInjection;

// 1. Configure services
var services = new ServiceCollection();

services.AddMessageQueue(options =>
{
    options.Capacity = 10000;
    options.EnablePersistence = true;
    options.PersistencePath = "./queue-data";
});

// 2. Register message handler
services.AddMessageHandler<OrderMessage, OrderMessageHandler>(options =>
{
    options.MaxParallelism = 5;
    options.Timeout = TimeSpan.FromMinutes(2);
    options.MaxRetries = 3;
});

// 3. Build and start
var serviceProvider = services.BuildServiceProvider();
var queueHost = serviceProvider.GetRequiredService<IPersistentQueueHost>();
await queueHost.StartAsync();

// 4. Publish message
var publisher = serviceProvider.GetRequiredService<IQueuePublisher>();
await publisher.PublishAsync(new OrderMessage
{
    OrderId = "ORDER-123",
    CustomerId = "CUST-456"
});
```

### Define Message and Handler

```csharp
// Message class
public class OrderMessage
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }
}

// Handler class
public class OrderMessageHandler : IMessageHandler<OrderMessage>
{
    private readonly ILogger<OrderMessageHandler> _logger;
    private readonly IOrderService _orderService;

    public OrderMessageHandler(
        ILogger<OrderMessageHandler> logger,
        IOrderService orderService)
    {
        _logger = logger;
        _orderService = orderService;
    }

    public async Task HandleAsync(OrderMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing order {OrderId}", message.OrderId);

        await _orderService.ProcessOrderAsync(message.OrderId, cancellationToken);

        _logger.LogInformation("Order {OrderId} processed successfully", message.OrderId);
    }
}
```

## Core APIs

### IQueuePublisher

Primary interface for publishing messages to the queue.

#### PublishAsync

```csharp
Task<Guid> PublishAsync<TMessage>(
    TMessage message,
    string? deduplicationKey = null,
    string? correlationId = null,
    CancellationToken cancellationToken = default);
```

**Parameters**:
- `message`: The message object to publish
- `deduplicationKey`: Optional key for deduplication (prevents duplicate processing)
- `correlationId`: Optional correlation ID for tracing
- `cancellationToken`: Cancellation token

**Returns**: Message ID (GUID)

**Example**:
```csharp
// Simple publish
var messageId = await publisher.PublishAsync(new OrderMessage
{
    OrderId = "ORDER-123"
});

// With deduplication
var messageId = await publisher.PublishAsync(
    new OrderMessage { OrderId = "ORDER-123" },
    deduplicationKey: "ORDER-123"); // Prevents duplicate processing

// With correlation tracking
var messageId = await publisher.PublishAsync(
    new OrderMessage { OrderId = "ORDER-123" },
    correlationId: "TRACE-789");
```

### IQueueManager

Low-level queue management interface (advanced use).

#### EnqueueAsync

```csharp
Task<Guid> EnqueueAsync<TMessage>(
    TMessage message,
    string? deduplicationKey = null,
    string? correlationId = null,
    CancellationToken cancellationToken = default);
```

#### CheckoutAsync

```csharp
Task<MessageEnvelope<TMessage>?> CheckoutAsync<TMessage>(
    string handlerId,
    TimeSpan? leaseDuration = null,
    CancellationToken cancellationToken = default);
```

**Example**:
```csharp
// Manually checkout message
var envelope = await queueManager.CheckoutAsync<OrderMessage>(
    handlerId: "worker-1",
    leaseDuration: TimeSpan.FromMinutes(5));

if (envelope != null)
{
    try
    {
        // Process message
        await ProcessOrder(envelope.Message);

        // Acknowledge successful processing
        await queueManager.AcknowledgeAsync(envelope.MessageId);
    }
    catch (Exception ex)
    {
        // Requeue for retry
        await queueManager.RequeueAsync(envelope.MessageId, failureReason: ex.Message);
    }
}
```

#### AcknowledgeAsync

```csharp
Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default);
```

#### RequeueAsync

```csharp
Task RequeueAsync(
    Guid messageId,
    string? failureReason = null,
    CancellationToken cancellationToken = default);
```

#### ExtendLeaseAsync

```csharp
Task ExtendLeaseAsync(
    Guid messageId,
    TimeSpan extensionDuration,
    CancellationToken cancellationToken = default);
```

**Example - Long-Running Handler**:
```csharp
public async Task HandleAsync(OrderMessage message, CancellationToken cancellationToken)
{
    var messageId = /* get current message ID */;
    var leaseMonitor = /* inject ILeaseMonitor */;

    for (int i = 0; i < 100; i++)
    {
        // Do some work
        await ProcessBatch(i, cancellationToken);

        // Extend lease every 10 batches to prevent timeout
        if (i % 10 == 0)
        {
            await leaseMonitor.ExtendLeaseAsync(
                messageId,
                TimeSpan.FromMinutes(5),
                cancellationToken);
        }
    }
}
```

## Handler APIs

### IMessageHandler<TMessage>

Interface that all message handlers must implement.

```csharp
public interface IMessageHandler<TMessage>
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken);
}
```

**Example - Simple Handler**:
```csharp
public class EmailHandler : IMessageHandler<SendEmailMessage>
{
    private readonly IEmailService _emailService;

    public EmailHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task HandleAsync(SendEmailMessage message, CancellationToken cancellationToken)
    {
        await _emailService.SendAsync(
            message.To,
            message.Subject,
            message.Body,
            cancellationToken);
    }
}
```

**Example - Handler with Retry Logic**:
```csharp
public class PaymentHandler : IMessageHandler<ProcessPaymentMessage>
{
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<PaymentHandler> _logger;

    public PaymentHandler(IPaymentGateway gateway, ILogger<PaymentHandler> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task HandleAsync(ProcessPaymentMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _gateway.ChargeAsync(
                message.CustomerId,
                message.Amount,
                cancellationToken);

            if (!result.Success)
            {
                // Throw to trigger retry
                throw new PaymentFailedException(result.ErrorMessage);
            }

            _logger.LogInformation("Payment processed: {TransactionId}", result.TransactionId);
        }
        catch (TemporaryPaymentException ex)
        {
            // Transient error - let it retry
            _logger.LogWarning(ex, "Payment failed temporarily, will retry");
            throw;
        }
        catch (PermanentPaymentException ex)
        {
            // Permanent error - log and don't retry
            _logger.LogError(ex, "Payment failed permanently");
            // Don't rethrow - this will acknowledge and not retry
        }
    }
}
```

### HandlerOptions<TMessage>

Configuration for individual message handlers.

```csharp
services.Configure<HandlerOptions<OrderMessage>>(options =>
{
    // Parallelism
    options.MaxParallelism = 10;        // Max concurrent instances
    options.MinParallelism = 2;         // Initial worker count

    // Timeouts
    options.Timeout = TimeSpan.FromMinutes(5);
    options.LeaseDuration = TimeSpan.FromMinutes(5);

    // Retry behavior
    options.MaxRetries = 3;
    options.BackoffStrategy = RetryBackoffStrategy.Exponential;
    options.InitialBackoff = TimeSpan.FromSeconds(1);
    options.MaxBackoff = TimeSpan.FromMinutes(5);

    // Lease extension for long-running handlers
    options.EnableLeaseExtension = true;

    // Deduplication
    options.DeduplicationMode = DeduplicationMode.Strict;
});
```

## Admin APIs

### IQueueAdminApi

Administrative interface for monitoring and managing the queue.

#### GetMetricsAsync

```csharp
Task<QueueMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
```

**Example**:
```csharp
var metrics = await adminApi.GetMetricsAsync();

Console.WriteLine($"Total Enqueued: {metrics.TotalEnqueued}");
Console.WriteLine($"Total Processed: {metrics.TotalProcessed}");
Console.WriteLine($"Pending: {metrics.PendingMessages}");
Console.WriteLine($"In-Flight: {metrics.InFlightMessages}");
Console.WriteLine($"Dead Letter: {metrics.DeadLetterCount}");
Console.WriteLine($"Avg Latency: {metrics.AverageLatency}");
```

#### GetHandlerMetricsAsync

```csharp
Task<Dictionary<string, HandlerMetricsSnapshot>> GetHandlerMetricsAsync(
    CancellationToken cancellationToken = default);
```

**Example**:
```csharp
var handlerMetrics = await adminApi.GetHandlerMetricsAsync();

foreach (var (messageType, metrics) in handlerMetrics)
{
    Console.WriteLine($"\nHandler: {messageType}");
    Console.WriteLine($"  Active Workers: {metrics.ActiveWorkers}");
    Console.WriteLine($"  Processed: {metrics.TotalProcessed}");
    Console.WriteLine($"  Failed: {metrics.TotalFailed}");
    Console.WriteLine($"  Avg Time: {metrics.AverageProcessingTimeMs}ms");
    Console.WriteLine($"  Throughput: {metrics.MessagesPerSecond:F2} msg/s");
}
```

#### ScaleHandlerAsync<TMessage>

```csharp
Task ScaleHandlerAsync<TMessage>(int instanceCount, CancellationToken cancellationToken = default);
```

**Example**:
```csharp
// Scale up during peak hours
await adminApi.ScaleHandlerAsync<OrderMessage>(20);

// Scale down during off-hours
await adminApi.ScaleHandlerAsync<OrderMessage>(2);
```

#### TriggerSnapshotAsync

```csharp
Task TriggerSnapshotAsync(CancellationToken cancellationToken = default);
```

**Example**:
```csharp
// Manual snapshot before maintenance
await adminApi.TriggerSnapshotAsync();
```

### IDeadLetterQueue

Interface for managing failed messages.

#### GetMessagesAsync

```csharp
Task<IEnumerable<DeadLetterEnvelope>> GetMessagesAsync(
    Type? messageType = null,
    int limit = 100,
    CancellationToken cancellationToken = default);
```

**Example**:
```csharp
// Get all DLQ messages
var dlqMessages = await deadLetterQueue.GetMessagesAsync(limit: 1000);

// Get DLQ messages for specific type
var orderDlqMessages = await deadLetterQueue.GetMessagesAsync(
    messageType: typeof(OrderMessage),
    limit: 100);

// Inspect failures
foreach (var msg in dlqMessages)
{
    Console.WriteLine($"Message ID: {msg.MessageId}");
    Console.WriteLine($"Type: {msg.MessageType}");
    Console.WriteLine($"Failure: {msg.FailureReason}");
    Console.WriteLine($"Exception: {msg.ExceptionMessage}");
    Console.WriteLine($"Retries: {msg.RetryCount}");
    Console.WriteLine($"Failed At: {msg.FailureTimestamp}");
    Console.WriteLine();
}
```

#### ReplayAsync

```csharp
Task ReplayAsync(
    Guid messageId,
    bool resetRetryCount = true,
    CancellationToken cancellationToken = default);
```

**Example**:
```csharp
// Replay after fixing bug
await adminApi.ReplayDeadLetterAsync(messageId, resetRetryCount: true);

// Replay with existing retry count
await deadLetterQueue.ReplayAsync(messageId, resetRetryCount: false);
```

#### PurgeAsync

```csharp
Task PurgeAsync(
    TimeSpan olderThan = default,
    CancellationToken cancellationToken = default);
```

**Example**:
```csharp
// Purge all DLQ messages
await deadLetterQueue.PurgeAsync();

// Purge messages older than 30 days
await adminApi.PurgeDeadLetterQueueAsync(olderThan: TimeSpan.FromDays(30));
```

## Advanced Features

### Handler Chaining

Chain multiple message handlers together using `IQueuePublisher`.

```csharp
public class OrderCreatedHandler : IMessageHandler<OrderCreatedMessage>
{
    private readonly IQueuePublisher _publisher;
    private readonly IOrderService _orderService;

    public OrderCreatedHandler(IQueuePublisher publisher, IOrderService orderService)
    {
        _publisher = publisher;
        _orderService = orderService;
    }

    public async Task HandleAsync(OrderCreatedMessage message, CancellationToken cancellationToken)
    {
        // Process order
        var order = await _orderService.CreateOrderAsync(message.OrderId, cancellationToken);

        // Chain: Publish inventory reservation message
        await _publisher.PublishAsync(
            new ReserveInventoryMessage
            {
                OrderId = order.Id,
                Items = order.Items
            },
            correlationId: message.CorrelationId);

        // Chain: Publish payment processing message
        await _publisher.PublishAsync(
            new ProcessPaymentMessage
            {
                OrderId = order.Id,
                Amount = order.TotalAmount
            },
            correlationId: message.CorrelationId);
    }
}
```

### Heartbeat for Long-Running Handlers

```csharp
public class BatchProcessingHandler : IMessageHandler<BatchProcessMessage>
{
    private readonly IHeartbeatService _heartbeatService;
    private readonly IBatchService _batchService;

    public BatchProcessingHandler(
        IHeartbeatService heartbeatService,
        IBatchService batchService)
    {
        _heartbeatService = heartbeatService;
        _batchService = batchService;
    }

    public async Task HandleAsync(BatchProcessMessage message, CancellationToken cancellationToken)
    {
        var totalItems = message.ItemCount;
        var processedItems = 0;

        while (processedItems < totalItems)
        {
            // Process batch
            var batchSize = Math.Min(100, totalItems - processedItems);
            await _batchService.ProcessBatchAsync(
                message.BatchId,
                processedItems,
                batchSize,
                cancellationToken);

            processedItems += batchSize;

            // Send heartbeat with progress
            await _heartbeatService.HeartbeatAsync(
                message.MessageId,
                progressPercentage: (int)((double)processedItems / totalItems * 100),
                progressMessage: $"Processed {processedItems}/{totalItems} items");
        }
    }
}
```

### Custom Error Handling

```csharp
public class ResilientHandler : IMessageHandler<MyMessage>
{
    public async Task HandleAsync(MyMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessMessageAsync(message, cancellationToken);
        }
        catch (TransientException ex)
        {
            // Retry - throw to trigger automatic retry with backoff
            throw;
        }
        catch (PermanentException ex)
        {
            // Don't retry - log and acknowledge
            _logger.LogError(ex, "Permanent error processing message");
            // Don't rethrow - message will be acknowledged
        }
        catch (Exception ex) when (ex.Message.Contains("timeout"))
        {
            // Specific handling for timeout
            _logger.LogWarning("Timeout detected, will retry");
            throw;
        }
    }
}
```

## Examples

### Complete ASP.NET Core Integration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add MessageQueue
builder.Services.AddMessageQueue(options =>
{
    options.Capacity = 100000;
    options.EnablePersistence = true;
    options.PersistencePath = builder.Configuration["MessageQueue:DataPath"] ?? "./queue-data";
    options.DefaultMaxRetries = 5;
    options.DefaultTimeout = TimeSpan.FromMinutes(2);
});

// Register handlers
builder.Services.AddMessageHandler<SendEmailMessage, EmailHandler>(options =>
{
    options.MaxParallelism = 10;
    options.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddMessageHandler<ProcessOrderMessage, OrderHandler>(options =>
{
    options.MaxParallelism = 5;
    options.Timeout = TimeSpan.FromMinutes(5);
    options.EnableLeaseExtension = true;
});

// Add background service to start queue
builder.Services.AddHostedService<QueueHostedService>();

var app = builder.Build();

// API endpoint to publish messages
app.MapPost("/api/orders", async (CreateOrderRequest request, IQueuePublisher publisher) =>
{
    var messageId = await publisher.PublishAsync(new ProcessOrderMessage
    {
        OrderId = request.OrderId,
        CustomerId = request.CustomerId,
        Items = request.Items
    });

    return Results.Ok(new { MessageId = messageId });
});

// Admin endpoint for metrics
app.MapGet("/api/admin/metrics", async (IQueueAdminApi adminApi) =>
{
    var queueMetrics = await adminApi.GetMetricsAsync();
    var handlerMetrics = await adminApi.GetHandlerMetricsAsync();

    return Results.Ok(new { Queue = queueMetrics, Handlers = handlerMetrics });
});

app.Run();

// QueueHostedService.cs
public class QueueHostedService : IHostedService
{
    private readonly IPersistentQueueHost _queueHost;

    public QueueHostedService(IPersistentQueueHost queueHost)
    {
        _queueHost = queueHost;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _queueHost.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _queueHost.StopAsync(cancellationToken);
    }
}
```

## Best Practices

1. **Idempotency**: Design handlers to be idempotent (safe to retry)
2. **Deduplication Keys**: Use meaningful keys (order ID, transaction ID, etc.)
3. **Correlation IDs**: Propagate correlation IDs for tracing
4. **Timeouts**: Set realistic timeouts based on expected processing time
5. **Parallelism**: Start conservative, scale based on monitoring
6. **Error Handling**: Distinguish transient vs. permanent failures
7. **Monitoring**: Regularly review metrics and DLQ
8. **Testing**: Test failure scenarios and recovery
