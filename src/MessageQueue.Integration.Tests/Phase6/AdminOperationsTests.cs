// -----------------------------------------------------------------------
// <copyright file="AdminOperationsTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Integration.Tests.Phase6;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Models;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

[TestClass]
public class AdminOperationsTests
{
    private IQueueManager queueManager = null!;
    private IHandlerDispatcher dispatcher = null!;
    private IDeadLetterQueue dlq = null!;
    private IQueueAdminApi adminApi = null!;
    private QueueOptions options = null!;
    private IServiceProvider serviceProvider = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        var buffer = new CircularBuffer(100);
        var dedupIndex = new DeduplicationIndex();
        this.options = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromMinutes(5),
            DefaultMaxRetries = 3
        };

        this.queueManager = new QueueManager(buffer, dedupIndex, this.options);
        this.dlq = new DeadLetterQueue(this.queueManager, this.options);

        services.AddSingleton<IQueueManager>(this.queueManager);
        services.AddScoped<IMessageHandler<TestMessage>, TestMessageHandler>();

        this.serviceProvider = services.BuildServiceProvider();

        var registry = new HandlerRegistry(this.serviceProvider);
        registry.RegisterHandler<TestMessage, TestMessageHandler>();

        this.dispatcher = new HandlerDispatcher(this.queueManager, registry, this.serviceProvider.GetRequiredService<IServiceScopeFactory>());
        this.adminApi = new QueueAdminApi(this.queueManager, this.dispatcher, this.dlq, null);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (this.dispatcher != null)
        {
            await this.dispatcher.StopAsync();
        }
    }

    [TestMethod]
    public async Task AdminApi_GetMetrics_ReturnsQueueMetrics()
    {
        // Arrange
        await this.queueManager.EnqueueAsync(new TestMessage { Id = 1 });
        await this.queueManager.EnqueueAsync(new TestMessage { Id = 2 });

        // Act
        var metrics = await this.adminApi.GetMetricsAsync();

        // Assert
        metrics.Should().NotBeNull();
        metrics.ReadyCount.Should().Be(2);
        metrics.TotalCapacity.Should().Be(100);
    }

    [TestMethod]
    public async Task AdminApi_GetHandlerMetrics_ReturnsEmptyWhenNoProcessing()
    {
        // Act
        var handlerMetrics = await this.adminApi.GetHandlerMetricsAsync();

        // Assert
        handlerMetrics.Should().NotBeNull();
        handlerMetrics.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AdminApi_ScaleHandler_ChangesWorkerCount()
    {
        // Arrange
        await this.dispatcher.StartAsync();
        await Task.Delay(100); // Let initial workers start

        // Act - Scale up
        await this.adminApi.ScaleHandlerAsync<TestMessage>(5);
        await Task.Delay(200);

        var stats = await this.dispatcher.GetStatisticsAsync(typeof(TestMessage));

        // Assert
        stats.Should().NotBeNull();
        stats.ActiveWorkers.Should().BeGreaterOrEqualTo(1);
    }

    [TestMethod]
    public async Task AdminApi_ReplayDeadLetter_RequeulesFailedMessage()
    {
        // Arrange
        var envelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(TestMessage).AssemblyQualifiedName!,
            Payload = System.Text.Json.JsonSerializer.Serialize(new TestMessage { Id = 99 }),
            Status = MessageStatus.DeadLetter,
            RetryCount = 3,
            MaxRetries = 3
        };

        await this.dlq.AddAsync(envelope, "Max retries exceeded");

        // Act
        await this.adminApi.ReplayDeadLetterAsync(envelope.MessageId, resetRetryCount: true);

        // Assert
        var dlqMessages = await this.dlq.GetMessagesAsync();
        dlqMessages.Should().BeEmpty();

        var queueCount = await this.queueManager.GetCountAsync();
        queueCount.Should().BeGreaterOrEqualTo(1);
    }

    [TestMethod]
    public async Task AdminApi_PurgeDeadLetterQueue_RemovesAllMessages()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var envelope = new MessageEnvelope
            {
                MessageId = Guid.NewGuid(),
                MessageType = typeof(TestMessage).AssemblyQualifiedName!,
                Payload = $"{{\"Id\":{i}}}",
                Status = MessageStatus.DeadLetter
            };
            await this.dlq.AddAsync(envelope, $"Failed {i}");
        }

        // Act
        await this.adminApi.PurgeDeadLetterQueueAsync();

        // Assert
        var messages = await this.dlq.GetMessagesAsync();
        messages.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AdminApi_PurgeDeadLetterQueue_WithOlderThan_RemovesOldMessages()
    {
        // Arrange - Add old message
        var oldEnvelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(TestMessage).AssemblyQualifiedName!,
            Payload = "{\"Id\":1}"
        };
        await this.dlq.AddAsync(oldEnvelope, "Old failure");

        await Task.Delay(200);

        // Add new message
        var newEnvelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(TestMessage).AssemblyQualifiedName!,
            Payload = "{\"Id\":2}"
        };
        await this.dlq.AddAsync(newEnvelope, "New failure");

        // Act - Purge messages older than 100ms
        await this.adminApi.PurgeDeadLetterQueueAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        var remaining = await this.dlq.GetMessagesAsync();
        remaining.Should().HaveCount(1);
        remaining.Should().Contain(m => m.MessageId == newEnvelope.MessageId);
    }

    [TestMethod]
    public async Task AdminApi_ReplayDeadLetter_NonExistent_ThrowsException()
    {
        // Act & Assert
        var act = async () => await this.adminApi.ReplayDeadLetterAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task AdminApi_WithNoPersistence_TriggerSnapshot_ThrowsException()
    {
        // Act & Assert
        var act = async () => await this.adminApi.TriggerSnapshotAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Persistence is not configured*");
    }

    [TestMethod]
    public async Task AdminApi_WithNoDLQ_ReplayDeadLetter_ThrowsException()
    {
        // Arrange - Create admin API without DLQ
        var adminApiNoDlq = new QueueAdminApi(this.queueManager, this.dispatcher, null, null);

        // Act & Assert
        var act = async () => await adminApiNoDlq.ReplayDeadLetterAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Dead-letter queue is not configured*");
    }

    // Test message and handler
    private class TestMessage
    {
        public int Id { get; set; }
    }

    private class TestMessageHandler : IMessageHandler<TestMessage>
    {
        public Task HandleAsync(TestMessage message, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
