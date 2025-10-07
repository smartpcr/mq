# MessageQueue.Samples.Basic

## Purpose

Basic usage examples demonstrating simple message enqueue, handler registration, and message processing.

## What's Included

- Simple message definition
- Basic handler implementation
- Queue host configuration
- Message enqueue examples
- Graceful shutdown

## Running the Sample

```bash
dotnet run --project src/samples/MessageQueue.Samples.Basic/MessageQueue.Samples.Basic.csproj
```

## Code Overview

```csharp
// Define message
public class OrderMessage
{
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
}

// Implement handler
public class OrderHandler : IMessageHandler<OrderMessage>
{
    public async Task HandleAsync(OrderMessage message, CancellationToken ct)
    {
        Console.WriteLine($"Processing order {message.OrderId}");
        await Task.Delay(100, ct); // Simulate work
    }
}

// Configure and run
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMessageQueue();
        services.AddScoped<IMessageHandler<OrderMessage>, OrderHandler>();
    })
    .Build();

await host.RunAsync();
```

## Target Audience

Developers new to the Message Queue system looking to understand basic concepts.
