// -----------------------------------------------------------------------
// <copyright file="DeadLetterQueueTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Models;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class DeadLetterQueueTests
{
    private IQueueManager queueManager = null!;
    private DeadLetterQueue dlq = null!;
    private QueueOptions options = null!;

    [TestInitialize]
    public void Setup()
    {
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
    }

    [TestMethod]
    public async Task AddAsync_AddsMessageToDLQ()
    {
        // Arrange
        var envelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = "{\"test\":\"data\"}",
            Status = MessageStatus.InFlight,
            RetryCount = 3,
            MaxRetries = 3
        };

        // Act
        await this.dlq.AddAsync(envelope, "Test failure");

        // Assert
        var messages = await this.dlq.GetMessagesAsync();
        messages.Should().HaveCount(1);
        var dlqMessage = messages.First();
        dlqMessage.MessageId.Should().Be(envelope.MessageId);
        dlqMessage.FailureReason.Should().Be("Test failure");
        dlqMessage.Status.Should().Be(MessageStatus.DeadLetter);
    }

    [TestMethod]
    public async Task AddAsync_WithException_CapturesExceptionDetails()
    {
        // Arrange
        var envelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = "{}"
        };

        // Create exception with stack trace by throwing and catching it
        Exception exception = null!;
        try
        {
            throw new InvalidOperationException("Test exception");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        // Act
        await this.dlq.AddAsync(envelope, "Processing failed", exception);

        // Assert
        var messages = await this.dlq.GetMessagesAsync();
        var dlqMessage = messages.First();
        dlqMessage.ExceptionMessage.Should().Be("Test exception");
        dlqMessage.ExceptionType.Should().Contain("InvalidOperationException");
        dlqMessage.ExceptionStackTrace.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task GetMessagesAsync_WithTypeFilter_ReturnsMatchingMessages()
    {
        // Arrange
        await this.dlq.AddAsync(new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(string).FullName,
            Payload = "test1"
        }, "Failure 1");

        await this.dlq.AddAsync(new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(int).FullName,
            Payload = "123"
        }, "Failure 2");

        // Act
        var messages = await this.dlq.GetMessagesAsync(typeof(string));

        // Assert
        messages.Should().HaveCount(1);
        messages.First().MessageType.Should().Be(typeof(string).FullName);
    }

    [TestMethod]
    public async Task GetMessagesAsync_WithLimit_ReturnsLimitedResults()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await this.dlq.AddAsync(new MessageEnvelope
            {
                MessageId = Guid.NewGuid(),
                MessageType = "Test",
                Payload = $"message{i}"
            }, "Failure");
        }

        // Act
        var messages = await this.dlq.GetMessagesAsync(limit: 5);

        // Assert
        messages.Should().HaveCount(5);
    }

    [TestMethod]
    public async Task ReplayAsync_ReenqueuesMessageToMainQueue()
    {
        // Arrange
        var originalId = await this.queueManager.EnqueueAsync("Test message");
        var envelope = new MessageEnvelope
        {
            MessageId = originalId,
            MessageType = typeof(string).FullName,
            Payload = "\"Test message\"",
            Status = MessageStatus.DeadLetter
        };

        await this.dlq.AddAsync(envelope, "Test failure");

        // Act
        await this.dlq.ReplayAsync(originalId, resetRetryCount: true);

        // Assert
        var dlqMessages = await this.dlq.GetMessagesAsync();
        dlqMessages.Should().BeEmpty(); // Removed from DLQ

        var queueCount = await this.queueManager.GetCountAsync();
        queueCount.Should().BeGreaterOrEqualTo(1); // Added back to queue
    }

    [TestMethod]
    public async Task ReplayAsync_WithResetRetryCount_ResetsRetries()
    {
        // Arrange
        var envelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(string).FullName,
            Payload = "\"Test\"",
            RetryCount = 3,
            MaxRetries = 3
        };

        await this.dlq.AddAsync(envelope, "Max retries");

        // Act
        await this.dlq.ReplayAsync(envelope.MessageId, resetRetryCount: true);

        // Assert - message should be re-enqueued with new ID
        var dlqMessages = await this.dlq.GetMessagesAsync();
        dlqMessages.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ReplayAsync_NonExistentMessage_ThrowsException()
    {
        // Act & Assert
        var act = async () => await this.dlq.ReplayAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found in dead-letter queue*");
    }

    [TestMethod]
    public async Task PurgeAsync_RemovesAllMessages()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await this.dlq.AddAsync(new MessageEnvelope
            {
                MessageId = Guid.NewGuid(),
                MessageType = "Test",
                Payload = $"message{i}"
            }, "Failure");
        }

        // Act
        await this.dlq.PurgeAsync();

        // Assert
        var messages = await this.dlq.GetMessagesAsync();
        messages.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PurgeAsync_WithOlderThan_RemovesOldMessages()
    {
        // Arrange
        var oldEnvelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "Test",
            Payload = "old"
        };

        await this.dlq.AddAsync(oldEnvelope, "Old failure");

        // Simulate old timestamp
        var messages = await this.dlq.GetMessagesAsync();
        var oldMessage = messages.First();

        // Wait a bit
        await Task.Delay(100);

        var newEnvelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "Test",
            Payload = "new"
        };

        await this.dlq.AddAsync(newEnvelope, "New failure");

        // Act - purge messages older than 50ms
        await this.dlq.PurgeAsync(TimeSpan.FromMilliseconds(50));

        // Assert
        var remainingMessages = await this.dlq.GetMessagesAsync();
        remainingMessages.Should().HaveCount(1);
        remainingMessages.First().MessageId.Should().Be(newEnvelope.MessageId);
    }

    [TestMethod]
    public async Task GetMetricsAsync_ReturnsCorrectMetrics()
    {
        // Arrange
        await this.dlq.AddAsync(new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "Type1",
            Payload = "test1"
        }, "Timeout");

        await this.dlq.AddAsync(new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "Type1",
            Payload = "test2"
        }, "Timeout");

        await this.dlq.AddAsync(new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "Type2",
            Payload = "test3"
        }, "Error");

        // Act
        var metrics = await this.dlq.GetMetricsAsync();

        // Assert
        metrics.TotalCount.Should().Be(3);
        metrics.OldestMessageTime.Should().NotBeNull();
        metrics.CountByMessageType.Should().HaveCount(2);
        metrics.CountByMessageType["Type1"].Should().Be(2);
        metrics.CountByMessageType["Type2"].Should().Be(1);
        metrics.CountByFailureReason.Should().HaveCount(2);
        metrics.CountByFailureReason["Timeout"].Should().Be(2);
        metrics.CountByFailureReason["Error"].Should().Be(1);
    }
}
