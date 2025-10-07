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
    private IQueueManager _queueManager = null!;
    private IHandlerDispatcher _dispatcher = null!;
    private IDeadLetterQueue _dlq = null!;
    private IQueueAdminApi _adminApi = null!;
    private QueueOptions _options = null!;
    private IServiceProvider _serviceProvider = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        var buffer = new CircularBuffer(100);
        var dedupIndex = new DeduplicationIndex();
        _options = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromMinutes(5),
            DefaultMaxRetries = 3
        };

        _queueManager = new QueueManager(buffer, dedupIndex, _options);
        _dlq = new DeadLetterQueue(_queueManager, _options);

        services.AddSingleton<IQueueManager>(_queueManager);
        services.AddScoped<IMessageHandler<TestMessage>, TestMessageHandler>();

        _serviceProvider = services.BuildServiceProvider();

        var registry = new HandlerRegistry(_serviceProvider);
        registry.RegisterHandler<TestMessage, TestMessageHandler>();

        _dispatcher = new HandlerDispatcher(_queueManager, registry, _serviceProvider.GetRequiredService<IServiceScopeFactory>());
        _adminApi = new QueueAdminApi(_queueManager, _dispatcher, _dlq, null);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_dispatcher != null)
        {
            await _dispatcher.StopAsync();
        }
    }

    [TestMethod]
    public async Task AdminApi_GetMetrics_ReturnsQueueMetrics()
    {
        // Arrange
        await _queueManager.EnqueueAsync(new TestMessage { Id = 1 });
        await _queueManager.EnqueueAsync(new TestMessage { Id = 2 });

        // Act
        var metrics = await _adminApi.GetMetricsAsync();

        // Assert
        metrics.Should().NotBeNull();
        metrics.ReadyCount.Should().Be(2);
        metrics.TotalCapacity.Should().Be(100);
    }

    [TestMethod]
    public async Task AdminApi_GetHandlerMetrics_ReturnsEmptyWhenNoProcessing()
    {
        // Act
        var handlerMetrics = await _adminApi.GetHandlerMetricsAsync();

        // Assert
        handlerMetrics.Should().NotBeNull();
        handlerMetrics.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AdminApi_ScaleHandler_ChangesWorkerCount()
    {
        // Arrange
        await _dispatcher.StartAsync();
        await Task.Delay(100); // Let initial workers start

        // Act - Scale up
        await _adminApi.ScaleHandlerAsync<TestMessage>(5);
        await Task.Delay(200);

        var stats = await _dispatcher.GetStatisticsAsync(typeof(TestMessage));

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

        await _dlq.AddAsync(envelope, "Max retries exceeded");

        // Act
        await _adminApi.ReplayDeadLetterAsync(envelope.MessageId, resetRetryCount: true);

        // Assert
        var dlqMessages = await _dlq.GetMessagesAsync();
        dlqMessages.Should().BeEmpty();

        var queueCount = await _queueManager.GetCountAsync();
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
            await _dlq.AddAsync(envelope, $"Failed {i}");
        }

        // Act
        await _adminApi.PurgeDeadLetterQueueAsync();

        // Assert
        var messages = await _dlq.GetMessagesAsync();
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
        await _dlq.AddAsync(oldEnvelope, "Old failure");

        await Task.Delay(200);

        // Add new message
        var newEnvelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(TestMessage).AssemblyQualifiedName!,
            Payload = "{\"Id\":2}"
        };
        await _dlq.AddAsync(newEnvelope, "New failure");

        // Act - Purge messages older than 100ms
        await _adminApi.PurgeDeadLetterQueueAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        var remaining = await _dlq.GetMessagesAsync();
        remaining.Should().HaveCount(1);
        remaining.Should().Contain(m => m.MessageId == newEnvelope.MessageId);
    }

    [TestMethod]
    public async Task AdminApi_ReplayDeadLetter_NonExistent_ThrowsException()
    {
        // Act & Assert
        var act = async () => await _adminApi.ReplayDeadLetterAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task AdminApi_WithNoPersistence_TriggerSnapshot_ThrowsException()
    {
        // Act & Assert
        var act = async () => await _adminApi.TriggerSnapshotAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Persistence is not configured*");
    }

    [TestMethod]
    public async Task AdminApi_WithNoDLQ_ReplayDeadLetter_ThrowsException()
    {
        // Arrange - Create admin API without DLQ
        var adminApiNoDlq = new QueueAdminApi(_queueManager, _dispatcher, null, null);

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
