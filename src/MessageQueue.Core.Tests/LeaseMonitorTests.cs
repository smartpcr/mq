// -----------------------------------------------------------------------
// <copyright file="LeaseMonitorTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class LeaseMonitorTests
{
    private IQueueManager queueManager = null!;
    private LeaseMonitor leaseMonitor = null!;
    private QueueOptions options = null!;

    [TestInitialize]
    public void Setup()
    {
        var buffer = new CircularBuffer(100);
        var dedupIndex = new DeduplicationIndex();
        this.options = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromSeconds(1),
            DefaultMaxRetries = 3,
            DefaultBackoffStrategy = RetryBackoffStrategy.None // Disable backoff for tests
        };

        // For LeaseMonitor tests, we don't need DLQ functionality
        // Pass null for DLQ to avoid circular dependency issues
        this.queueManager = new QueueManager(buffer, dedupIndex, this.options, null, null);
        this.leaseMonitor = new LeaseMonitor(this.queueManager, this.options);
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
    public async Task StartAsync_StartsMonitor()
    {
        // Act
        await this.leaseMonitor.StartAsync();

        // Assert - no exception
        await this.leaseMonitor.StopAsync();
    }

    [TestMethod]
    public async Task StartAsync_WhenAlreadyRunning_ThrowsException()
    {
        // Arrange
        await this.leaseMonitor.StartAsync();

        // Act & Assert
        var act = async () => await this.leaseMonitor.StartAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Lease monitor is already running.");

        await this.leaseMonitor.StopAsync();
    }

    [TestMethod]
    public async Task StopAsync_WhenNotRunning_DoesNotThrow()
    {
        // Act & Assert
        var act = async () => await this.leaseMonitor.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task CheckExpiredLeasesAsync_RequeulesExpiredMessages()
    {
        // Arrange
        var messageId = await this.queueManager.EnqueueAsync("Test message");
        var checkedOut = await this.queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(100));
        checkedOut.Should().NotBeNull();

        // Wait for lease to expire
        await Task.Delay(150);

        // Act
        await this.leaseMonitor.CheckExpiredLeasesAsync();

        // Assert - message should be back in Ready state
        var checkedOut2 = await this.queueManager.CheckoutAsync<string>("worker-2");
        checkedOut2.Should().NotBeNull();
        checkedOut2.Message.Should().Be("Test message");
    }

    [TestMethod]
    public async Task CheckExpiredLeasesAsync_IgnoresActiveLeases()
    {
        // Arrange
        var messageId = await this.queueManager.EnqueueAsync("Test message");
        var checkedOut = await this.queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMinutes(5));
        checkedOut.Should().NotBeNull();

        // Act
        await this.leaseMonitor.CheckExpiredLeasesAsync();

        // Assert - message should still be checked out
        var checkedOut2 = await this.queueManager.CheckoutAsync<string>("worker-2");
        checkedOut2.Should().BeNull(); // No messages available
    }

    [TestMethod]
    public async Task ExtendLeaseAsync_ExtendsMessageLease()
    {
        // Arrange
        var messageId = await this.queueManager.EnqueueAsync("Test message");
        var checkedOut = await this.queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(200));
        checkedOut.Should().NotBeNull();

        // Wait 100ms
        await Task.Delay(100);

        // Act - extend lease
        await this.leaseMonitor.ExtendLeaseAsync(messageId, TimeSpan.FromMinutes(1));

        // Wait another 150ms (original would have expired)
        await Task.Delay(150);

        // Assert - message should still be checked out (not expired)
        await this.leaseMonitor.CheckExpiredLeasesAsync();
        var checkedOut2 = await this.queueManager.CheckoutAsync<string>("worker-2");
        checkedOut2.Should().BeNull(); // Still checked out
    }

    [TestMethod]
    public async Task MonitorLoop_AutomaticallyChecksExpiredLeases()
    {
        // Arrange
        await this.leaseMonitor.StartAsync();

        var messageId = await this.queueManager.EnqueueAsync("Test message");
        var checkedOut = await this.queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(500));
        checkedOut.Should().NotBeNull();

        // Act - wait for monitor to detect and requeue
        // Wait longer than lease expiry (500ms) + check interval buffer
        await Task.Delay(1500);

        // Assert - message should be requeued automatically
        var checkedOut2 = await this.queueManager.CheckoutAsync<string>("worker-2");
        checkedOut2.Should().NotBeNull();
        checkedOut2.Message.Should().Be("Test message");

        await this.leaseMonitor.StopAsync();
    }

    [TestMethod]
    public async Task MonitorLoop_HandlesMultipleExpiredLeases()
    {
        // Arrange
        await this.leaseMonitor.StartAsync();

        // Enqueue and checkout multiple messages
        await this.queueManager.EnqueueAsync("Message 1");
        await this.queueManager.EnqueueAsync("Message 2");
        await this.queueManager.EnqueueAsync("Message 3");

        var msg1 = await this.queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(300));
        var msg2 = await this.queueManager.CheckoutAsync<string>("worker-2", TimeSpan.FromMilliseconds(300));
        var msg3 = await this.queueManager.CheckoutAsync<string>("worker-3", TimeSpan.FromMilliseconds(300));

        msg1.Should().NotBeNull();
        msg2.Should().NotBeNull();
        msg3.Should().NotBeNull();

        // Act - wait for all leases to expire
        // Wait longer than lease expiry (300ms) + check interval (1s)
        await Task.Delay(2000);

        // Assert - all messages should be requeued
        var requeuedMsg1 = await this.queueManager.CheckoutAsync<string>("worker-4");
        var requeuedMsg2 = await this.queueManager.CheckoutAsync<string>("worker-5");
        var requeuedMsg3 = await this.queueManager.CheckoutAsync<string>("worker-6");

        requeuedMsg1.Should().NotBeNull();
        requeuedMsg2.Should().NotBeNull();
        requeuedMsg3.Should().NotBeNull();

        await this.leaseMonitor.StopAsync();
    }
}
