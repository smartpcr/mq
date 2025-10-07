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
    private IQueueManager _queueManager = null!;
    private LeaseMonitor _leaseMonitor = null!;
    private QueueOptions _options = null!;

    [TestInitialize]
    public void Setup()
    {
        var buffer = new CircularBuffer(100);
        var dedupIndex = new DeduplicationIndex();
        _options = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromSeconds(1),
            DefaultMaxRetries = 3
        };

        var dlq = new DeadLetterQueue(null, _options);
        _queueManager = new QueueManager(buffer, dedupIndex, _options, null, dlq);
        _leaseMonitor = new LeaseMonitor(_queueManager, _options);
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
    public async Task StartAsync_StartsMonitor()
    {
        // Act
        await _leaseMonitor.StartAsync();

        // Assert - no exception
        await _leaseMonitor.StopAsync();
    }

    [TestMethod]
    public async Task StartAsync_WhenAlreadyRunning_ThrowsException()
    {
        // Arrange
        await _leaseMonitor.StartAsync();

        // Act & Assert
        var act = async () => await _leaseMonitor.StartAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Lease monitor is already running.");

        await _leaseMonitor.StopAsync();
    }

    [TestMethod]
    public async Task StopAsync_WhenNotRunning_DoesNotThrow()
    {
        // Act & Assert
        var act = async () => await _leaseMonitor.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task CheckExpiredLeasesAsync_RequeulesExpiredMessages()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync("Test message");
        var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(100));
        checkedOut.Should().NotBeNull();

        // Wait for lease to expire
        await Task.Delay(150);

        // Act
        await _leaseMonitor.CheckExpiredLeasesAsync();

        // Assert - message should be back in Ready state
        var checkedOut2 = await _queueManager.CheckoutAsync<string>("worker-2");
        checkedOut2.Should().NotBeNull();
        checkedOut2.Message.Should().Be("Test message");
    }

    [TestMethod]
    public async Task CheckExpiredLeasesAsync_IgnoresActiveLeases()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync("Test message");
        var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMinutes(5));
        checkedOut.Should().NotBeNull();

        // Act
        await _leaseMonitor.CheckExpiredLeasesAsync();

        // Assert - message should still be checked out
        var checkedOut2 = await _queueManager.CheckoutAsync<string>("worker-2");
        checkedOut2.Should().BeNull(); // No messages available
    }

    [TestMethod]
    public async Task ExtendLeaseAsync_ExtendsMessageLease()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync("Test message");
        var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(200));
        checkedOut.Should().NotBeNull();

        // Wait 100ms
        await Task.Delay(100);

        // Act - extend lease
        await _leaseMonitor.ExtendLeaseAsync(messageId, TimeSpan.FromMinutes(1));

        // Wait another 150ms (original would have expired)
        await Task.Delay(150);

        // Assert - message should still be checked out (not expired)
        await _leaseMonitor.CheckExpiredLeasesAsync();
        var checkedOut2 = await _queueManager.CheckoutAsync<string>("worker-2");
        checkedOut2.Should().BeNull(); // Still checked out
    }

    [TestMethod]
    public async Task MonitorLoop_AutomaticallyChecksExpiredLeases()
    {
        // Arrange
        await _leaseMonitor.StartAsync();

        var messageId = await _queueManager.EnqueueAsync("Test message");
        var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(500));
        checkedOut.Should().NotBeNull();

        // Act - wait for monitor to detect and requeue
        await Task.Delay(1000);

        // Assert - message should be requeued automatically
        var checkedOut2 = await _queueManager.CheckoutAsync<string>("worker-2");
        checkedOut2.Should().NotBeNull();
        checkedOut2.Message.Should().Be("Test message");

        await _leaseMonitor.StopAsync();
    }

    [TestMethod]
    public async Task MonitorLoop_HandlesMultipleExpiredLeases()
    {
        // Arrange
        await _leaseMonitor.StartAsync();

        // Enqueue and checkout multiple messages
        await _queueManager.EnqueueAsync("Message 1");
        await _queueManager.EnqueueAsync("Message 2");
        await _queueManager.EnqueueAsync("Message 3");

        var msg1 = await _queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(300));
        var msg2 = await _queueManager.CheckoutAsync<string>("worker-2", TimeSpan.FromMilliseconds(300));
        var msg3 = await _queueManager.CheckoutAsync<string>("worker-3", TimeSpan.FromMilliseconds(300));

        msg1.Should().NotBeNull();
        msg2.Should().NotBeNull();
        msg3.Should().NotBeNull();

        // Act - wait for all leases to expire
        await Task.Delay(1000);

        // Assert - all messages should be requeued
        var requeuedMsg1 = await _queueManager.CheckoutAsync<string>("worker-4");
        var requeuedMsg2 = await _queueManager.CheckoutAsync<string>("worker-5");
        var requeuedMsg3 = await _queueManager.CheckoutAsync<string>("worker-6");

        requeuedMsg1.Should().NotBeNull();
        requeuedMsg2.Should().NotBeNull();
        requeuedMsg3.Should().NotBeNull();

        await _leaseMonitor.StopAsync();
    }
}
