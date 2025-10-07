namespace MessageQueue.Core.Tests;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class CircularBufferTests
{
    [TestMethod]
    public async Task EnqueueAsync_WithValidMessage_AddsToBuffer()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var envelope = CreateTestEnvelope();

        // Act
        bool result = await buffer.EnqueueAsync(envelope);

        // Assert
        result.Should().BeTrue();
        var count = await buffer.GetCountAsync();
        count.Should().Be(1);
    }

    [TestMethod]
    public async Task EnqueueAsync_WhenFull_DropsOldestAndAddsNew()
    {
        // Arrange
        var buffer = new CircularBuffer(2);
        var msg1 = CreateTestEnvelope();
        var msg2 = CreateTestEnvelope();
        var msg3 = CreateTestEnvelope();

        await buffer.EnqueueAsync(msg1);
        await buffer.EnqueueAsync(msg2);

        // Act - buffer is full, should drop oldest (msg1) and add msg3
        bool result = await buffer.EnqueueAsync(msg3);

        // Assert
        result.Should().BeTrue();
        var count = await buffer.GetCountAsync();
        count.Should().Be(2); // Still 2 messages

        var messages = await buffer.GetAllMessagesAsync();
        messages.Should().NotContain(m => m.MessageId == msg1.MessageId); // msg1 dropped
        messages.Should().Contain(m => m.MessageId == msg2.MessageId); // msg2 still there
        messages.Should().Contain(m => m.MessageId == msg3.MessageId); // msg3 added
    }

    [TestMethod]
    public async Task CheckoutAsync_WithReadyMessage_ReturnsEnvelope()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var envelope = CreateTestEnvelope();
        await buffer.EnqueueAsync(envelope);

        // Act
        var checkedOut = await buffer.CheckoutAsync(
            typeof(string),
            "worker-1",
            TimeSpan.FromMinutes(5));

        // Assert
        checkedOut.Should().NotBeNull();
        checkedOut!.MessageId.Should().Be(envelope.MessageId);
        checkedOut.Status.Should().Be(MessageStatus.InFlight);
        checkedOut.Lease.Should().NotBeNull();
        checkedOut.Lease!.HandlerId.Should().Be("worker-1");
    }

    [TestMethod]
    public async Task CheckoutAsync_WithNoReadyMessages_ReturnsNull()
    {
        // Arrange
        var buffer = new CircularBuffer(10);

        // Act
        var checkedOut = await buffer.CheckoutAsync(
            typeof(string),
            "worker-1",
            TimeSpan.FromMinutes(5));

        // Assert
        checkedOut.Should().BeNull();
    }

    [TestMethod]
    public async Task AcknowledgeAsync_WithValidMessageId_RemovesMessage()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var envelope = CreateTestEnvelope();
        await buffer.EnqueueAsync(envelope);
        var checkedOut = await buffer.CheckoutAsync(
            typeof(string),
            "worker-1",
            TimeSpan.FromMinutes(5));

        // Act
        bool acknowledged = await buffer.AcknowledgeAsync(checkedOut!.MessageId);

        // Assert
        acknowledged.Should().BeTrue();
        var count = await buffer.GetCountAsync();
        count.Should().Be(0);
    }

    [TestMethod]
    public async Task RequeueAsync_IncrementsRetryCount()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var envelope = CreateTestEnvelope();
        envelope.RetryCount = 0;
        await buffer.EnqueueAsync(envelope);
        var checkedOut = await buffer.CheckoutAsync(
            typeof(string),
            "worker-1",
            TimeSpan.FromMinutes(5));

        // Act
        bool requeued = await buffer.RequeueAsync(checkedOut!.MessageId);

        // Assert
        requeued.Should().BeTrue();
        var messages = await buffer.GetAllMessagesAsync();
        var requeuedMessage = messages[0];
        requeuedMessage.RetryCount.Should().Be(1);
        requeuedMessage.Status.Should().Be(MessageStatus.Ready);
    }

    [TestMethod]
    public async Task ReplaceAsync_WithInFlightMessage_SupersedesAndEnqueuesNew()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var envelope = CreateTestEnvelope("dedup-key-1");
        await buffer.EnqueueAsync(envelope);
        await buffer.CheckoutAsync(typeof(string), "worker-1", TimeSpan.FromMinutes(5));

        var newEnvelope = CreateTestEnvelope("dedup-key-1");
        newEnvelope.Payload = "{\"new\":\"data\"}";

        // Act
        bool replaced = await buffer.ReplaceAsync(newEnvelope, "dedup-key-1");

        // Assert
        replaced.Should().BeTrue();
        var messages = await buffer.GetAllMessagesAsync();
        messages.Should().Contain(m => m.MessageId == newEnvelope.MessageId);
        messages.Should().Contain(m => m.IsSuperseded);
    }

    [TestMethod]
    public async Task GetAllMessagesAsync_ReturnsNonCompletedMessages()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        await buffer.EnqueueAsync(CreateTestEnvelope());
        await buffer.EnqueueAsync(CreateTestEnvelope());

        // Act
        var messages = await buffer.GetAllMessagesAsync();

        // Assert
        messages.Should().HaveCount(2);
        messages.Should().OnlyContain(m => m.Status != MessageStatus.Completed);
    }

    [TestMethod]
    public async Task RemoveAsync_WithValidMessageId_RemovesMessage()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var envelope = CreateTestEnvelope();
        await buffer.EnqueueAsync(envelope);

        // Act
        bool removed = await buffer.RemoveAsync(envelope.MessageId);

        // Assert
        removed.Should().BeTrue();
        var count = await buffer.GetCountAsync();
        count.Should().Be(0);
    }

    [TestMethod]
    public async Task ConcurrentEnqueue_HandlesMultipleThreads()
    {
        // Arrange
        var buffer = new CircularBuffer(100);
        var tasks = new Task<bool>[50];

        // Act
        for (int i = 0; i < 50; i++)
        {
            tasks[i] = Task.Run(async () => await buffer.EnqueueAsync(CreateTestEnvelope()));
        }

        await Task.WhenAll(tasks);

        // Assert
        tasks.Should().OnlyContain(t => t.Result == true);
        var count = await buffer.GetCountAsync();
        count.Should().Be(50);
    }

    private static MessageEnvelope CreateTestEnvelope(string? deduplicationKey = null)
    {
        return new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(string).FullName!,
            Payload = "{\"test\":\"data\"}",
            DeduplicationKey = deduplicationKey,
            Status = MessageStatus.Ready,
            RetryCount = 0,
            MaxRetries = 3,
            Lease = null,
            LastPersistedVersion = 0,
            Metadata = new MessageMetadata
            {
                CorrelationId = null,
                Headers = new System.Collections.Generic.Dictionary<string, string>(),
                Source = "Test",
                Version = 1
            },
            EnqueuedAt = DateTime.UtcNow,
            IsSuperseded = false
        };
    }
}
