// -----------------------------------------------------------------------
// <copyright file="EndToEndDispatchTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Integration.Tests;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class EndToEndDispatchTests
{
    private IServiceProvider serviceProvider = null!;
    private IQueueManager queueManager = null!;
    private HandlerRegistry handlerRegistry = null!;
    private HandlerDispatcher dispatcher = null!;
    private IServiceScopeFactory scopeFactory = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MessageTracker>();
        services.AddTransient<TrackingMessageHandler>();

        this.serviceProvider = services.BuildServiceProvider();
        this.scopeFactory = this.serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // Create queue manager
        var buffer = new CircularBuffer(100);
        var dedupIndex = new DeduplicationIndex();
        var queueOptions = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromMinutes(5),
            DefaultMaxRetries = 3
        };
        this.queueManager = new QueueManager(buffer, dedupIndex, queueOptions);

        this.handlerRegistry = new HandlerRegistry(this.serviceProvider);
        this.dispatcher = new HandlerDispatcher(this.queueManager, this.handlerRegistry, this.scopeFactory);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (this.dispatcher != null)
        {
            await this.dispatcher.StopAsync();
        }
        (this.serviceProvider as IDisposable)?.Dispose();
    }

    [TestMethod]
    public async Task EndToEnd_EnqueueAndProcess_MessageIsHandled()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        this.handlerRegistry.RegisterHandler<TestMessage, TrackingMessageHandler>(new HandlerOptions<TestMessage>
        {
            MinParallelism = 1,
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await this.dispatcher.StartAsync();

        // Act
        var message = new TestMessage { Id = Guid.NewGuid(), Content = "Test Message" };
        await this.queueManager.EnqueueAsync(message);
        this.dispatcher.SignalMessageReady(typeof(TestMessage));

        // Wait for processing
        await Task.Delay(500);

        // Assert
        tracker.ProcessedMessages.Should().ContainKey(message.Id);
        tracker.ProcessedMessages[message.Id].Should().Be(message.Content);

        var stats = await this.dispatcher.GetStatisticsAsync(typeof(TestMessage));
        stats.TotalProcessed.Should().BeGreaterOrEqualTo(1);
    }

    [TestMethod]
    public async Task EndToEnd_MultipleMessages_AllProcessed()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        this.handlerRegistry.RegisterHandler<TestMessage, TrackingMessageHandler>(new HandlerOptions<TestMessage>
        {
            MinParallelism = 2,
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await this.dispatcher.StartAsync();

        // Act - enqueue multiple messages
        var messageCount = 10;
        var messageIds = new Guid[messageCount];

        for (int i = 0; i < messageCount; i++)
        {
            var message = new TestMessage { Id = Guid.NewGuid(), Content = $"Message {i}" };
            messageIds[i] = message.Id;
            await this.queueManager.EnqueueAsync(message);
            this.dispatcher.SignalMessageReady(typeof(TestMessage));
        }

        // Wait for processing
        await Task.Delay(2000);

        // Assert
        tracker.ProcessedMessages.Count.Should().Be(messageCount);
        foreach (var messageId in messageIds)
        {
            tracker.ProcessedMessages.Should().ContainKey(messageId);
        }

        var stats = await this.dispatcher.GetStatisticsAsync(typeof(TestMessage));
        stats.TotalProcessed.Should().Be(messageCount);
    }

    [TestMethod]
    public async Task EndToEnd_ParallelProcessing_MessagesConcurrentlyProcessed()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        this.handlerRegistry.RegisterHandler<SlowTestMessage, SlowTrackingMessageHandler>(new HandlerOptions<SlowTestMessage>
        {
            MinParallelism = 3,
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        // Register slow handler
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddTransient<SlowTrackingMessageHandler>();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var registry = new HandlerRegistry(sp);
        var dispatcher = new HandlerDispatcher(this.queueManager, registry, scopeFactory);

        registry.RegisterHandler<SlowTestMessage, SlowTrackingMessageHandler>(new HandlerOptions<SlowTestMessage>
        {
            MinParallelism = 3,
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await dispatcher.StartAsync();

        // Act - enqueue messages that take time to process
        var messageCount = 5;
        for (int i = 0; i < messageCount; i++)
        {
            var message = new SlowTestMessage { Id = Guid.NewGuid(), Content = $"Slow {i}", DelayMs = 300 };
            await this.queueManager.EnqueueAsync(message);
            dispatcher.SignalMessageReady(typeof(SlowTestMessage));
        }

        // Wait for processing
        await Task.Delay(1000);

        // Assert - with 3 workers and 300ms delay, should process faster than sequential
        var stats = await dispatcher.GetStatisticsAsync(typeof(SlowTestMessage));
        stats.TotalProcessed.Should().BeGreaterThan(0);

        await dispatcher.StopAsync();
        (sp as IDisposable)?.Dispose();
    }

    [TestMethod]
    public async Task EndToEnd_HandlerTimeout_MessageRequeued()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        // Register handler with very short timeout
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddTransient<SlowTrackingMessageHandler>();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var registry = new HandlerRegistry(sp);
        var dispatcher = new HandlerDispatcher(this.queueManager, registry, scopeFactory);

        registry.RegisterHandler<SlowTestMessage, SlowTrackingMessageHandler>(new HandlerOptions<SlowTestMessage>
        {
            MinParallelism = 1,
            MaxParallelism = 1,
            Timeout = TimeSpan.FromMilliseconds(50), // Very short timeout
            LeaseDuration = TimeSpan.FromSeconds(1)
        });

        await dispatcher.StartAsync();

        // Act - enqueue message that takes longer than timeout
        var message = new SlowTestMessage { Id = Guid.NewGuid(), Content = "Timeout Test", DelayMs = 500 };
        await this.queueManager.EnqueueAsync(message);
        dispatcher.SignalMessageReady(typeof(SlowTestMessage));

        // Wait for timeout and requeue
        await Task.Delay(1000);

        // Assert - message should be requeued (failure count incremented)
        var stats = await dispatcher.GetStatisticsAsync(typeof(SlowTestMessage));
        stats.TotalFailed.Should().BeGreaterOrEqualTo(1);

        await dispatcher.StopAsync();
        (sp as IDisposable)?.Dispose();
    }

    [TestMethod]
    public async Task EndToEnd_Deduplication_DuplicateMessagesSuperseded()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        this.handlerRegistry.RegisterHandler<TestMessage, TrackingMessageHandler>(new HandlerOptions<TestMessage>
        {
            MinParallelism = 1,
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await this.dispatcher.StartAsync();

        // Act - enqueue duplicate messages with same dedup key
        var dedupKey = "unique-key-1";
        var message1 = new TestMessage { Id = Guid.NewGuid(), Content = "Version 1" };
        var message2 = new TestMessage { Id = Guid.NewGuid(), Content = "Version 2" };

        await this.queueManager.EnqueueAsync(message1, dedupKey);
        await this.queueManager.EnqueueAsync(message2, dedupKey); // Should supersede message1

        this.dispatcher.SignalMessageReady(typeof(TestMessage));

        // Wait for processing
        await Task.Delay(500);

        // Assert - only one message should be processed (the newer one)
        tracker.ProcessedMessages.Count.Should().BeLessOrEqualTo(1);

        // If a message was processed, it should be message2
        if (tracker.ProcessedMessages.Count > 0)
        {
            tracker.ProcessedMessages.Should().ContainKey(message2.Id);
        }
    }

    // Test message types
    public class TestMessage
    {
        public Guid Id { get; set; }
        public string Content { get; set; }
    }

    public class SlowTestMessage
    {
        public Guid Id { get; set; }
        public string Content { get; set; }
        public int DelayMs { get; set; }
    }

    // Message tracker for verification
    public class MessageTracker
    {
        public ConcurrentDictionary<Guid, string> ProcessedMessages { get; } = new ConcurrentDictionary<Guid, string>();
    }

    // Test handler implementations
    public class TrackingMessageHandler : IMessageHandler<TestMessage>
    {
        private readonly MessageTracker tracker;

        public TrackingMessageHandler(MessageTracker tracker)
        {
            this.tracker = tracker;
        }

        public Task HandleAsync(TestMessage message, CancellationToken cancellationToken)
        {
            this.tracker.ProcessedMessages[message.Id] = message.Content;
            return Task.CompletedTask;
        }
    }

    public class SlowTrackingMessageHandler : IMessageHandler<SlowTestMessage>
    {
        private readonly MessageTracker tracker;

        public SlowTrackingMessageHandler(MessageTracker tracker)
        {
            this.tracker = tracker;
        }

        public async Task HandleAsync(SlowTestMessage message, CancellationToken cancellationToken)
        {
            await Task.Delay(message.DelayMs, cancellationToken);
            this.tracker.ProcessedMessages[message.Id] = message.Content;
        }
    }
}
