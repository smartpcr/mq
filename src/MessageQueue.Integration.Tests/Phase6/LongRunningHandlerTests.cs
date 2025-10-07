namespace MessageQueue.Integration.Tests.Phase6;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class LongRunningHandlerTests
{
    private IQueueManager _queueManager = null!;
    private ILeaseMonitor _leaseMonitor = null!;
    private IHeartbeatService _heartbeatService = null!;
    private QueueOptions _options = null!;

    [TestInitialize]
    public void Setup()
    {
        var buffer = new CircularBuffer(100);
        var dedupIndex = new DeduplicationIndex();
        _options = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromSeconds(2),
            DefaultMaxRetries = 3
        };

        _queueManager = new QueueManager(buffer, dedupIndex, _options);
        _leaseMonitor = new LeaseMonitor(_queueManager, _options);
        _heartbeatService = new HeartbeatService(_queueManager, _leaseMonitor, _options);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_leaseMonitor != null)
        {
            await _leaseMonitor.StopAsync();
        }
    }

    [TestMethod]
    public async Task HeartbeatService_RecordsHeartbeat_Successfully()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-001" });
        var envelope = await _queueManager.CheckoutAsync<LongRunningTask>("worker-1");
        envelope.Should().NotBeNull();

        // Act
        await _heartbeatService.HeartbeatAsync(messageId, progressPercentage: 25, progressMessage: "Processing...");

        // Assert
        var progress = await _heartbeatService.GetProgressAsync(messageId);
        progress.Should().NotBeNull();
        progress!.ProgressPercentage.Should().Be(25);
        progress.ProgressMessage.Should().Be("Processing...");
        progress.HeartbeatCount.Should().Be(1);
    }

    [TestMethod]
    public async Task HeartbeatService_MultipleHeartbeats_UpdatesProgress()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-002" });
        var envelope = await _queueManager.CheckoutAsync<LongRunningTask>("worker-1");

        // Act - Send multiple heartbeats
        await _heartbeatService.HeartbeatAsync(messageId, 10, "Starting");
        await Task.Delay(100);
        await _heartbeatService.HeartbeatAsync(messageId, 50, "Halfway");
        await Task.Delay(100);
        await _heartbeatService.HeartbeatAsync(messageId, 90, "Almost done");

        // Assert
        var progress = await _heartbeatService.GetProgressAsync(messageId);
        progress.Should().NotBeNull();
        progress!.ProgressPercentage.Should().Be(90);
        progress.ProgressMessage.Should().Be("Almost done");
        progress.HeartbeatCount.Should().Be(3);
    }

    [TestMethod]
    public async Task HeartbeatService_ExtendsLease_PreventTimeout()
    {
        // Arrange
        await _leaseMonitor.StartAsync();

        var messageId = await _queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-003" });
        var envelope = await _queueManager.CheckoutAsync<LongRunningTask>("worker-1", TimeSpan.FromMilliseconds(500));
        envelope.Should().NotBeNull();

        // Act - Send heartbeat before lease expires
        await Task.Delay(300);
        await _heartbeatService.HeartbeatAsync(messageId, 50, "Still working");

        // Wait another 400ms (would have expired without heartbeat)
        await Task.Delay(400);

        // Assert - Message should still be checked out (lease extended)
        var status = await _queueManager.GetMessageAsync(messageId);
        status.Should().NotBeNull();

        var tryCheckout = await _queueManager.CheckoutAsync<LongRunningTask>("worker-2");
        tryCheckout.Should().BeNull(); // Still held by worker-1

        await _queueManager.AcknowledgeAsync(messageId);
    }

    [TestMethod]
    public async Task HeartbeatService_InvalidProgressPercentage_ThrowsException()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-004" });
        await _queueManager.CheckoutAsync<LongRunningTask>("worker-1");

        // Act & Assert - Progress > 100
        var act1 = async () => await _heartbeatService.HeartbeatAsync(messageId, 150);
        await act1.Should().ThrowAsync<ArgumentOutOfRangeException>();

        // Act & Assert - Progress < 0
        var act2 = async () => await _heartbeatService.HeartbeatAsync(messageId, -10);
        await act2.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task HeartbeatService_GetLastHeartbeat_ReturnsTimestamp()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-005" });
        await _queueManager.CheckoutAsync<LongRunningTask>("worker-1");

        var beforeHeartbeat = DateTime.UtcNow;

        // Act
        await _heartbeatService.HeartbeatAsync(messageId, 50);
        await Task.Delay(50);

        // Assert
        var lastHeartbeat = await _heartbeatService.GetLastHeartbeatAsync(messageId);
        lastHeartbeat.Should().NotBeNull();
        lastHeartbeat.Should().BeOnOrAfter(beforeHeartbeat);
    }

    [TestMethod]
    public async Task HeartbeatService_CompletedMessage_ThrowsException()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-006" });
        await _queueManager.CheckoutAsync<LongRunningTask>("worker-1");

        // Complete the message
        await _queueManager.AcknowledgeAsync(messageId);

        // Act & Assert - Heartbeat on completed message should fail
        var act = async () => await _heartbeatService.HeartbeatAsync(messageId, 100);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task LongRunningHandler_WithHeartbeats_CompletesSuccessfully()
    {
        // Arrange - Simulate long-running task
        await _leaseMonitor.StartAsync();

        var messageId = await _queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-007" });
        var envelope = await _queueManager.CheckoutAsync<LongRunningTask>("worker-1", TimeSpan.FromSeconds(1));

        // Act - Simulate long task with periodic heartbeats
        for (int i = 1; i <= 5; i++)
        {
            await Task.Delay(400); // Total 2 seconds (longer than initial 1s lease)
            await _heartbeatService.HeartbeatAsync(
                messageId,
                progressPercentage: i * 20,
                progressMessage: $"Step {i} of 5");
        }

        // Assert - Message still active despite time > original lease
        var progress = await _heartbeatService.GetProgressAsync(messageId);
        progress.Should().NotBeNull();
        progress!.ProgressPercentage.Should().Be(100);
        progress.HeartbeatCount.Should().Be(5);

        // Complete the task
        await _queueManager.AcknowledgeAsync(messageId);
    }

    // Test message class
    private class LongRunningTask
    {
        public string TaskId { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }
}
