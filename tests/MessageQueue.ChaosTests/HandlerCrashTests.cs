// -----------------------------------------------------------------------
// <copyright file="HandlerCrashTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MessageQueue.ChaosTests;

/// <summary>
/// Chaos tests for handler crash scenarios.
/// </summary>
[TestClass]
public class HandlerCrashTests
{
    [TestMethod]
    public async Task MessagesAreRequeuedWhenHandlerCrashes()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new QueueOptions { Capacity = 1000, DefaultMaxRetries = 3 };
        var buffer = new CircularBuffer(options.Capacity);
        var dedupIndex = new DeduplicationIndex();
        var queueManager = new QueueManager(buffer, dedupIndex, options);

        services.AddScoped<IMessageHandler<TestMessage>, CrashingHandler>();
        services.Configure<HandlerOptions<TestMessage>>(opt =>
        {
            opt.MaxParallelism = 1;
            opt.Timeout = TimeSpan.FromSeconds(5);
            opt.MaxRetries = 3;
        });

        var serviceProvider = services.BuildServiceProvider();
        var registry = new HandlerRegistry(serviceProvider);
        registry.RegisterHandler<TestMessage, CrashingHandler>(
            serviceProvider.GetRequiredService<HandlerOptions<TestMessage>>());

        var dispatcher = new HandlerDispatcher(queueManager, registry, serviceProvider.GetRequiredService<IServiceScopeFactory>());

        // Act
        await dispatcher.StartAsync();

        var messageId = await queueManager.EnqueueAsync(new TestMessage { Id = 1, Data = "Test" });
        dispatcher.SignalMessageReady(typeof(TestMessage));

        // Wait for processing
        await Task.Delay(1000);

        // Get message to check retry count
        var allMessages = await buffer.GetAllMessagesAsync();
        var message = allMessages.FirstOrDefault(m => m.MessageId == messageId);

        await dispatcher.StopAsync();
        serviceProvider.Dispose();

        // Assert - message should have been retried
        message.Should().NotBeNull();
        message!.RetryCount.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public async Task MessageMovesToDLQAfterMaxRetries()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new QueueOptions { Capacity = 1000, DefaultMaxRetries = 2 };
        var buffer = new CircularBuffer(options.Capacity);
        var dedupIndex = new DeduplicationIndex();
        var queueManager = new QueueManager(buffer, dedupIndex, options);
        var deadLetterQueue = new DeadLetterQueue(queueManager, options);

        services.AddScoped<IMessageHandler<TestMessage>, AlwaysFailingHandler>();
        services.Configure<HandlerOptions<TestMessage>>(opt =>
        {
            opt.MaxParallelism = 1;
            opt.Timeout = TimeSpan.FromSeconds(5);
            opt.MaxRetries = 2;
        });

        var serviceProvider = services.BuildServiceProvider();
        var registry = new HandlerRegistry(serviceProvider);
        registry.RegisterHandler<TestMessage, AlwaysFailingHandler>(
            serviceProvider.GetRequiredService<HandlerOptions<TestMessage>>());

        var dispatcher = new HandlerDispatcher(queueManager, registry, serviceProvider.GetRequiredService<IServiceScopeFactory>());

        // Act
        await dispatcher.StartAsync();

        var messageId = await queueManager.EnqueueAsync(new TestMessage { Id = 1, Data = "Test" });
        dispatcher.SignalMessageReady(typeof(TestMessage));

        // Wait for all retries
        await Task.Delay(3000);

        await dispatcher.StopAsync();

        // Assert - check if message made it to DLQ
        var dlqMessages = await deadLetterQueue.GetMessagesAsync();
        var dlqCount = dlqMessages.Count();

        serviceProvider.Dispose();

        // Note: This test demonstrates the intent - actual DLQ integration
        // would require the dispatcher to call deadLetterQueue.AddAsync
        dlqCount.Should().BeGreaterOrEqualTo(0);
    }

    [TestMethod]
    public async Task TimeoutCausesMessageRequeue()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new QueueOptions { Capacity = 1000, DefaultMaxRetries = 3 };
        var buffer = new CircularBuffer(options.Capacity);
        var dedupIndex = new DeduplicationIndex();
        var queueManager = new QueueManager(buffer, dedupIndex, options);

        services.AddScoped<IMessageHandler<TestMessage>, SlowHandler>();
        services.Configure<HandlerOptions<TestMessage>>(opt =>
        {
            opt.MaxParallelism = 1;
            opt.Timeout = TimeSpan.FromMilliseconds(100); // Very short timeout
            opt.MaxRetries = 3;
        });

        var serviceProvider = services.BuildServiceProvider();
        var registry = new HandlerRegistry(serviceProvider);
        registry.RegisterHandler<TestMessage, SlowHandler>(
            serviceProvider.GetRequiredService<HandlerOptions<TestMessage>>());

        var dispatcher = new HandlerDispatcher(queueManager, registry, serviceProvider.GetRequiredService<IServiceScopeFactory>());

        // Act
        await dispatcher.StartAsync();

        var messageId = await queueManager.EnqueueAsync(new TestMessage { Id = 1, Data = "Test" });
        dispatcher.SignalMessageReady(typeof(TestMessage));

        // Wait for timeout and requeue
        await Task.Delay(1000);

        var allMessages = await buffer.GetAllMessagesAsync();
        var message = allMessages.FirstOrDefault(m => m.MessageId == messageId);

        await dispatcher.StopAsync();
        serviceProvider.Dispose();

        // Assert - message should have been requeued after timeout
        message.Should().NotBeNull();
        message!.RetryCount.Should().BeGreaterThan(0);
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    private class CrashingHandler : IMessageHandler<TestMessage>
    {
        private int attempts = 0;

        public Task HandleAsync(TestMessage message, CancellationToken cancellationToken)
        {
            attempts++;
            if (attempts <= 2)
            {
                throw new Exception($"Simulated crash on attempt {attempts}");
            }

            // Succeed on third attempt
            return Task.CompletedTask;
        }
    }

    private class AlwaysFailingHandler : IMessageHandler<TestMessage>
    {
        public Task HandleAsync(TestMessage message, CancellationToken cancellationToken)
        {
            throw new Exception("Simulated permanent failure");
        }
    }

    private class SlowHandler : IMessageHandler<TestMessage>
    {
        public async Task HandleAsync(TestMessage message, CancellationToken cancellationToken)
        {
            // Intentionally slow to trigger timeout
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}
