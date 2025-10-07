// -----------------------------------------------------------------------
// <copyright file="LongRunningHandlerTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Integration.Tests;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class LongRunningHandlerTests
{
    private IQueueManager queueManager = null!;
    private ILeaseMonitor leaseMonitor = null!;
    private IHeartbeatService heartbeatService = null!;
    private QueueOptions options = null!;

    [TestInitialize]
    public void Setup()
    {
        var buffer = new CircularBuffer(100);
        var dedupIndex = new DeduplicationIndex();
        this.options = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromSeconds(2),
            DefaultMaxRetries = 3
        };

        this.queueManager = new QueueManager(buffer, dedupIndex, this.options);
        this.leaseMonitor = new LeaseMonitor(this.queueManager, this.options);
        this.heartbeatService = new HeartbeatService(this.queueManager, this.leaseMonitor, this.options);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (this.leaseMonitor != null)
        {
            await this.leaseMonitor.StopAsync();
        }
    }

    [TestMethod]
    public async Task HeartbeatService_RecordsHeartbeat_Successfully()
    {
        // Arrange
        var messageId = await this.queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-001" });
        var envelope = await this.queueManager.CheckoutAsync<LongRunningTask>("worker-1");
        envelope.Should().NotBeNull();

        // Act
        await this.heartbeatService.HeartbeatAsync(messageId, progressPercentage: 25, progressMessage: "Processing...");

        // Assert
        var progress = await this.heartbeatService.GetProgressAsync(messageId);
        progress.Should().NotBeNull();
        progress!.ProgressPercentage.Should().Be(25);
        progress.ProgressMessage.Should().Be("Processing...");
        progress.HeartbeatCount.Should().Be(1);
    }

    [TestMethod]
    public async Task HeartbeatService_MultipleHeartbeats_UpdatesProgress()
    {
        // Arrange
        var messageId = await this.queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-002" });
        var envelope = await this.queueManager.CheckoutAsync<LongRunningTask>("worker-1");

        // Act - Send multiple heartbeats
        await this.heartbeatService.HeartbeatAsync(messageId, 10, "Starting");
        await Task.Delay(100);
        await this.heartbeatService.HeartbeatAsync(messageId, 50, "Halfway");
        await Task.Delay(100);
        await this.heartbeatService.HeartbeatAsync(messageId, 90, "Almost done");

        // Assert
        var progress = await this.heartbeatService.GetProgressAsync(messageId);
        progress.Should().NotBeNull();
        progress!.ProgressPercentage.Should().Be(90);
        progress.ProgressMessage.Should().Be("Almost done");
        progress.HeartbeatCount.Should().Be(3);
    }

    [TestMethod]
    public async Task HeartbeatService_ExtendsLease_PreventTimeout()
    {
        // Arrange
        await this.leaseMonitor.StartAsync();

        var messageId = await this.queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-003" });
        var envelope = await this.queueManager.CheckoutAsync<LongRunningTask>("worker-1", TimeSpan.FromMilliseconds(500));
        envelope.Should().NotBeNull();

        // Act - Send heartbeat before lease expires
        await Task.Delay(300);
        await this.heartbeatService.HeartbeatAsync(messageId, 50, "Still working");

        // Wait another 400ms (would have expired without heartbeat)
        await Task.Delay(400);

        // Assert - Message should still be checked out (lease extended)
        var status = await this.queueManager.GetMessageAsync(messageId);
        status.Should().NotBeNull();

        var tryCheckout = await this.queueManager.CheckoutAsync<LongRunningTask>("worker-2");
        tryCheckout.Should().BeNull(); // Still held by worker-1

        await this.queueManager.AcknowledgeAsync(messageId);
    }

    [TestMethod]
    public async Task HeartbeatService_InvalidProgressPercentage_ThrowsException()
    {
        // Arrange
        var messageId = await this.queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-004" });
        await this.queueManager.CheckoutAsync<LongRunningTask>("worker-1");

        // Act & Assert - Progress > 100
        var act1 = async () => await this.heartbeatService.HeartbeatAsync(messageId, 150);
        await act1.Should().ThrowAsync<ArgumentOutOfRangeException>();

        // Act & Assert - Progress < 0
        var act2 = async () => await this.heartbeatService.HeartbeatAsync(messageId, -10);
        await act2.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task HeartbeatService_GetLastHeartbeat_ReturnsTimestamp()
    {
        // Arrange
        var messageId = await this.queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-005" });
        await this.queueManager.CheckoutAsync<LongRunningTask>("worker-1");

        var beforeHeartbeat = DateTime.UtcNow;

        // Act
        await this.heartbeatService.HeartbeatAsync(messageId, 50);
        await Task.Delay(50);

        // Assert
        var lastHeartbeat = await this.heartbeatService.GetLastHeartbeatAsync(messageId);
        lastHeartbeat.Should().NotBeNull();
        lastHeartbeat.Should().BeOnOrAfter(beforeHeartbeat);
    }

    [TestMethod]
    public async Task HeartbeatService_CompletedMessage_ThrowsException()
    {
        // Arrange
        var messageId = await this.queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-006" });
        await this.queueManager.CheckoutAsync<LongRunningTask>("worker-1");

        // Complete the message
        await this.queueManager.AcknowledgeAsync(messageId);

        // Act & Assert - Heartbeat on completed message should fail
        var act = async () => await this.heartbeatService.HeartbeatAsync(messageId, 100);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task LongRunningHandler_WithHeartbeats_CompletesSuccessfully()
    {
        // Arrange - Simulate long-running task
        await this.leaseMonitor.StartAsync();

        var messageId = await this.queueManager.EnqueueAsync(new LongRunningTask { TaskId = "TASK-007" });
        var envelope = await this.queueManager.CheckoutAsync<LongRunningTask>("worker-1", TimeSpan.FromSeconds(1));

        // Act - Simulate long task with periodic heartbeats
        for (int i = 1; i <= 5; i++)
        {
            await Task.Delay(400); // Total 2 seconds (longer than initial 1s lease)
            await this.heartbeatService.HeartbeatAsync(
                messageId,
                progressPercentage: i * 20,
                progressMessage: $"Step {i} of 5");
        }

        // Assert - Message still active despite time > original lease
        var progress = await this.heartbeatService.GetProgressAsync(messageId);
        progress.Should().NotBeNull();
        progress!.ProgressPercentage.Should().Be(100);
        progress.HeartbeatCount.Should().Be(5);

        // Complete the task
        await this.queueManager.AcknowledgeAsync(messageId);
    }

    // Test message class
    private class LongRunningTask
    {
        public string TaskId { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }
}
