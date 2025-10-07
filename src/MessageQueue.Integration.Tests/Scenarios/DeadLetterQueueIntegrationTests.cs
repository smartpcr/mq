namespace MessageQueue.Integration.Tests.Scenarios;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class DeadLetterQueueIntegrationTests
{
    private IQueueManager _queueManager = null!;
    private IDeadLetterQueue _dlq = null!;
    private ILeaseMonitor _leaseMonitor = null!;
    private QueueOptions _options = null!;

    [TestInitialize]
    public void Setup()
    {
        var buffer = new CircularBuffer(100);
        var dedupIndex = new DeduplicationIndex();
        _options = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromMinutes(5),
            DefaultMaxRetries = 3
        };

        _dlq = new DeadLetterQueue(null, _options); // We'll create QueueManager with DLQ next
        _queueManager = new QueueManager(buffer, dedupIndex, _options, null, _dlq);

        // Recreate DLQ with actual queue manager
        _dlq = new DeadLetterQueue(_queueManager, _options);

        // Recreate queue manager with proper DLQ
        _queueManager = new QueueManager(buffer, dedupIndex, _options, null, _dlq);

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
    public async Task EndToEnd_MaxRetriesExceeded_MovesToDLQ()
    {
        // Arrange
        var messageId = await _queueManager.EnqueueAsync("Test message");

        // Act - simulate max retries
        for (int i = 0; i < 4; i++) // 3 retries + 1 final attempt = 4
        {
            var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1");
            if (checkedOut == null)
                break;

            try
            {
                await _queueManager.RequeueAsync(checkedOut.MessageId, new Exception($"Attempt {i + 1} failed"));
            }
            catch (InvalidOperationException)
            {
                // Expected when max retries exceeded
                break;
            }
        }

        // Assert
        var dlqMessages = await _dlq.GetMessagesAsync();
        dlqMessages.Should().HaveCount(1);
        var dlqMessage = dlqMessages.First();
        dlqMessage.FailureReason.Should().Contain("Max retries exceeded");
        dlqMessage.RetryCount.Should().Be(3);
    }

    [TestMethod]
    public async Task EndToEnd_DLQReplay_ReenqueuesMessage()
    {
        // Arrange - move message to DLQ
        var messageId = await _queueManager.EnqueueAsync("Test message");

        for (int i = 0; i < 4; i++)
        {
            var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1");
            if (checkedOut == null)
                break;

            try
            {
                await _queueManager.RequeueAsync(checkedOut.MessageId, new Exception("Failed"));
            }
            catch (InvalidOperationException)
            {
                break;
            }
        }

        var dlqMessages = await _dlq.GetMessagesAsync();
        dlqMessages.Should().HaveCount(1);

        // Act - replay from DLQ
        await _dlq.ReplayAsync(dlqMessages.First().MessageId, resetRetryCount: true);

        // Assert
        var dlqAfterReplay = await _dlq.GetMessagesAsync();
        dlqAfterReplay.Should().BeEmpty();

        var replayedMessage = await _queueManager.CheckoutAsync<string>("worker-2");
        replayedMessage.Should().NotBeNull();
        replayedMessage.Message.Should().Be("Test message");
        replayedMessage.RetryCount.Should().Be(0); // Reset
    }

    [TestMethod]
    public async Task EndToEnd_LeaseExpiry_RequeulesMessage()
    {
        // Arrange
        await _leaseMonitor.StartAsync();

        var messageId = await _queueManager.EnqueueAsync("Test message");
        var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(500));
        checkedOut.Should().NotBeNull();

        // Act - wait for lease to expire and monitor to requeue
        await Task.Delay(1500);

        // Assert
        var requeuedMessage = await _queueManager.CheckoutAsync<string>("worker-2");
        requeuedMessage.Should().NotBeNull();
        requeuedMessage.Message.Should().Be("Test message");
        requeuedMessage.RetryCount.Should().Be(1); // Incremented due to requeue
    }

    [TestMethod]
    public async Task EndToEnd_LeaseExpiryWithMaxRetries_MovesToDLQ()
    {
        // Arrange
        await _leaseMonitor.StartAsync();

        var messageId = await _queueManager.EnqueueAsync("Test message");

        // Act - simulate lease expiry cycles until max retries
        for (int i = 0; i < 4; i++)
        {
            var checkedOut = await _queueManager.CheckoutAsync<string>($"worker-{i}", TimeSpan.FromMilliseconds(300));
            if (checkedOut == null)
                break;

            // Wait for lease to expire
            await Task.Delay(800);
        }

        // Additional wait for monitor to process
        await Task.Delay(500);

        // Assert
        var dlqMessages = await _dlq.GetMessagesAsync();
        dlqMessages.Should().HaveCount(1);
        dlqMessages.First().FailureReason.Should().Contain("Max retries exceeded");
    }

    [TestMethod]
    public async Task EndToEnd_DLQMetrics_TrackFailurePatterns()
    {
        // Arrange - add various failures
        await _queueManager.EnqueueAsync("Message 1");
        await _queueManager.EnqueueAsync("Message 2");
        await _queueManager.EnqueueAsync(123); // Different type

        // Simulate failures
        for (int i = 0; i < 3; i++)
        {
            var msg1 = await _queueManager.CheckoutAsync<string>("worker-1");
            if (msg1 != null)
            {
                try
                {
                    for (int retry = 0; retry < 4; retry++)
                    {
                        await _queueManager.RequeueAsync(msg1.MessageId, new TimeoutException("Timeout"));
                    }
                }
                catch { }
            }
        }

        var msg2 = await _queueManager.CheckoutAsync<int>("worker-2");
        if (msg2 != null)
        {
            try
            {
                for (int retry = 0; retry < 4; retry++)
                {
                    await _queueManager.RequeueAsync(msg2.MessageId, new InvalidOperationException("Invalid"));
                }
            }
            catch { }
        }

        // Act
        var metrics = await _dlq.GetMetricsAsync();

        // Assert
        metrics.TotalCount.Should().BeGreaterOrEqualTo(2);
        metrics.CountByMessageType.Should().ContainKey(typeof(string).FullName);
        metrics.CountByMessageType.Should().ContainKey(typeof(int).FullName);
        metrics.OldestMessageTime.Should().NotBeNull();
    }

    [TestMethod]
    public async Task EndToEnd_DLQPurge_RemovesOldMessages()
    {
        // Arrange - add message to DLQ
        var messageId = await _queueManager.EnqueueAsync("Old message");

        for (int i = 0; i < 4; i++)
        {
            var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1");
            if (checkedOut == null)
                break;

            try
            {
                await _queueManager.RequeueAsync(checkedOut.MessageId, new Exception("Failed"));
            }
            catch
            {
                break;
            }
        }

        var dlqMessages = await _dlq.GetMessagesAsync();
        dlqMessages.Should().HaveCount(1);

        // Wait a bit
        await Task.Delay(100);

        // Add another message
        var messageId2 = await _queueManager.EnqueueAsync("New message");
        for (int i = 0; i < 4; i++)
        {
            var checkedOut = await _queueManager.CheckoutAsync<string>("worker-2");
            if (checkedOut == null)
                break;

            try
            {
                await _queueManager.RequeueAsync(checkedOut.MessageId, new Exception("Failed"));
            }
            catch
            {
                break;
            }
        }

        // Act - purge old messages
        await _dlq.PurgeAsync(TimeSpan.FromMilliseconds(50));

        // Assert - only newer message should remain
        var remainingMessages = await _dlq.GetMessagesAsync();
        remainingMessages.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task EndToEnd_LeaseExtension_PreventsExpiry()
    {
        // Arrange
        await _leaseMonitor.StartAsync();

        var messageId = await _queueManager.EnqueueAsync("Long running task");
        var checkedOut = await _queueManager.CheckoutAsync<string>("worker-1", TimeSpan.FromMilliseconds(500));
        checkedOut.Should().NotBeNull();

        // Act - extend lease before expiry
        await Task.Delay(300);
        await _leaseMonitor.ExtendLeaseAsync(messageId, TimeSpan.FromMinutes(1));

        // Wait past original expiry
        await Task.Delay(500);

        // Assert - message should still be checked out
        var anotherCheckout = await _queueManager.CheckoutAsync<string>("worker-2");
        anotherCheckout.Should().BeNull(); // No messages available
    }
}
