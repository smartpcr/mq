// -----------------------------------------------------------------------
// <copyright file="DispatcherAutoSignalingTests.cs" company="Microsoft Corp.">
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

/// <summary>
/// Integration tests validating automatic dispatcher signaling (no manual SignalMessageReady calls).
/// These tests verify the channel-based notification system works end-to-end.
/// </summary>
[TestClass]
public class DispatcherAutoSignalingTests
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
        services.AddTransient<AlternativeMessageHandler>();

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

        this.handlerRegistry = new HandlerRegistry(this.serviceProvider);

        // Create queue manager WITHOUT dispatcher initially
        this.queueManager = new QueueManager(buffer, dedupIndex, queueOptions);

        // Create dispatcher
        this.dispatcher = new HandlerDispatcher(this.queueManager, this.handlerRegistry, this.scopeFactory);

        // Wire dispatcher to queue manager for auto-signaling
        this.queueManager.SetDispatcher(this.dispatcher);
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
    public async Task AutoSignaling_UnboundedChannel_ProcessesMessagesWithoutManualSignal()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        this.handlerRegistry.RegisterHandler<TestMessage, TrackingMessageHandler>(new HandlerOptions<TestMessage>
        {
            MinParallelism = 1,
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1),
            ChannelMode = ChannelMode.Unbounded
        });

        await this.dispatcher.StartAsync();

        // Act - Enqueue WITHOUT manual signal
        var message = new TestMessage { Id = Guid.NewGuid(), Content = "Auto-signal test" };
        await this.queueManager.EnqueueAsync(message);

        // Wait for automatic processing
        await Task.Delay(500);

        // Assert - Message should be automatically processed
        tracker.ProcessedMessages.Should().ContainKey(message.Id);
        tracker.ProcessedMessages[message.Id].Should().Be(message.Content);

        var stats = await this.dispatcher.GetStatisticsAsync(typeof(TestMessage));
        stats.TotalProcessed.Should().Be(1);
    }

    [TestMethod]
    public async Task AutoSignaling_BoundedCoalescingChannel_ProcessesMultipleMessages()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        this.handlerRegistry.RegisterHandler<TestMessage, TrackingMessageHandler>(new HandlerOptions<TestMessage>
        {
            MinParallelism = 3, // Multiple workers to handle signal coalescing
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1),
            ChannelMode = ChannelMode.BoundedCoalescing
        });

        await this.dispatcher.StartAsync();

        // Act - Rapidly enqueue multiple messages WITHOUT manual signals
        var messageCount = 10;
        var messageIds = new Guid[messageCount];

        for (int i = 0; i < messageCount; i++)
        {
            var message = new TestMessage { Id = Guid.NewGuid(), Content = $"Message {i}" };
            messageIds[i] = message.Id;
            await this.queueManager.EnqueueAsync(message);
            await Task.Delay(20); // Small delay to allow workers to process
        }

        // Wait for all messages to be processed
        await Task.Delay(2000);

        // Assert - All messages should be processed despite signal coalescing
        tracker.ProcessedMessages.Count.Should().Be(messageCount);
        foreach (var messageId in messageIds)
        {
            tracker.ProcessedMessages.Should().ContainKey(messageId);
        }

        var stats = await this.dispatcher.GetStatisticsAsync(typeof(TestMessage));
        stats.TotalProcessed.Should().Be(messageCount);
    }

    [TestMethod]
    public async Task AutoSignaling_MixedMessageTypes_EachTypeProcessedIndependently()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        // Register handlers for different message types
        this.handlerRegistry.RegisterHandler<TestMessage, TrackingMessageHandler>(new HandlerOptions<TestMessage>
        {
            MinParallelism = 2,
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1),
            ChannelMode = ChannelMode.Unbounded
        });

        this.handlerRegistry.RegisterHandler<AlternativeMessage, AlternativeMessageHandler>(new HandlerOptions<AlternativeMessage>
        {
            MinParallelism = 2,
            MaxParallelism = 5,
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1),
            ChannelMode = ChannelMode.BoundedCoalescing
        });

        await this.dispatcher.StartAsync();

        // Act - Enqueue messages of different types WITHOUT manual signals
        var testMsg1 = new TestMessage { Id = Guid.NewGuid(), Content = "Test 1" };
        var altMsg1 = new AlternativeMessage { Id = Guid.NewGuid(), Data = "Alt 1" };
        var testMsg2 = new TestMessage { Id = Guid.NewGuid(), Content = "Test 2" };
        var altMsg2 = new AlternativeMessage { Id = Guid.NewGuid(), Data = "Alt 2" };

        await this.queueManager.EnqueueAsync(testMsg1);
        await this.queueManager.EnqueueAsync(altMsg1);
        await this.queueManager.EnqueueAsync(testMsg2);
        await this.queueManager.EnqueueAsync(altMsg2);

        // Wait for processing
        await Task.Delay(1000);

        // Assert - All messages of both types should be processed
        tracker.ProcessedMessages.Should().ContainKey(testMsg1.Id);
        tracker.ProcessedMessages.Should().ContainKey(testMsg2.Id);
        tracker.ProcessedMessages.Should().ContainKey(altMsg1.Id);
        tracker.ProcessedMessages.Should().ContainKey(altMsg2.Id);

        var testStats = await this.dispatcher.GetStatisticsAsync(typeof(TestMessage));
        testStats.TotalProcessed.Should().Be(2);

        var altStats = await this.dispatcher.GetStatisticsAsync(typeof(AlternativeMessage));
        altStats.TotalProcessed.Should().Be(2);
    }

    [TestMethod]
    public async Task AutoSignaling_WithDeduplication_AutoSignalsReplacedMessage()
    {
        // Arrange
        var tracker = this.serviceProvider.GetRequiredService<MessageTracker>();

        this.handlerRegistry.RegisterHandler<TestMessage, TrackingMessageHandler>(new HandlerOptions<TestMessage>
        {
            MinParallelism = 1,
            MaxParallelism = 1, // Single worker
            Timeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromMinutes(1),
            ChannelMode = ChannelMode.Unbounded
        });

        // Start dispatcher first so auto-signals are captured
        await this.dispatcher.StartAsync();

        var dedupKey = "unique-key-auto-signal";
        var message1 = new TestMessage { Id = Guid.NewGuid(), Content = "Version 1" };
        var message2 = new TestMessage { Id = Guid.NewGuid(), Content = "Version 2" };

        // Act - Enqueue duplicate messages with same dedup key
        // Each enqueue auto-signals the dispatcher
        await this.queueManager.EnqueueAsync(message1, dedupKey);
        await Task.Delay(20); // Tiny delay
        await this.queueManager.EnqueueAsync(message2, dedupKey); // Should supersede message1

        // Wait for processing
        await Task.Delay(500);

        // Assert - At least one message processed (could be 1 or 2 depending on timing)
        // If message1 was checked out before message2 replaced it, both may be processed
        tracker.ProcessedMessages.Count.Should().BeGreaterOrEqualTo(1);
        if (tracker.ProcessedMessages.Count == 1)
        {
            // Only message2 processed (ideal case - dedup worked)
            tracker.ProcessedMessages.Should().ContainKey(message2.Id);
        }
        // Note: This test validates auto-signaling works with deduplication, not dedup semantics themselves
    }

    // Test message types
    public class TestMessage
    {
        public Guid Id { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class AlternativeMessage
    {
        public Guid Id { get; set; }
        public string Data { get; set; } = string.Empty;
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

    public class AlternativeMessageHandler : IMessageHandler<AlternativeMessage>
    {
        private readonly MessageTracker tracker;

        public AlternativeMessageHandler(MessageTracker tracker)
        {
            this.tracker = tracker;
        }

        public Task HandleAsync(AlternativeMessage message, CancellationToken cancellationToken)
        {
            this.tracker.ProcessedMessages[message.Id] = message.Data;
            return Task.CompletedTask;
        }
    }
}
