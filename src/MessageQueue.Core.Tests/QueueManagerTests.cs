namespace MessageQueue.Core.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class QueueManagerTests
{
    [TestMethod]
    public async Task EnqueueAsync_WithoutDeduplication_AddsMessage()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage = new TestMessage { Id = 1, Name = "Test" };

        // Act
        var messageId = await queueManager.EnqueueAsync(testMessage);

        // Assert
        messageId.Should().NotBeEmpty();
        var count = await queueManager.GetCountAsync();
        count.Should().Be(1);
    }

    [TestMethod]
    public async Task EnqueueAsync_WithDeduplication_SupersedesReadyMessage()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage1 = new TestMessage { Id = 1, Name = "Test1" };
        var testMessage2 = new TestMessage { Id = 2, Name = "Test2" };

        // Act
        var messageId1 = await queueManager.EnqueueAsync(testMessage1, deduplicationKey: "dedup-1");
        var messageId2 = await queueManager.EnqueueAsync(testMessage2, deduplicationKey: "dedup-1");

        // Assert - Second enqueue with same key should supersede the first (Ready) message
        messageId1.Should().NotBe(messageId2); // Different message IDs because supersede creates new message
        var count = await queueManager.GetCountAsync();
        count.Should().Be(1); // Only the new message counts (superseded messages excluded from count)
    }

    [TestMethod]
    public async Task EnqueueAsync_WithDeduplicationSupersede_ReplacesInFlightMessage()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage1 = new TestMessage { Id = 1, Name = "Original" };
        var testMessage2 = new TestMessage { Id = 2, Name = "Updated" };

        // Enqueue and checkout first message
        await queueManager.EnqueueAsync(testMessage1, deduplicationKey: "dedup-1");
        await queueManager.CheckoutAsync<TestMessage>("worker-1");

        // Act - Enqueue second message with same dedup key (should supersede)
        var messageId2 = await queueManager.EnqueueAsync(testMessage2, deduplicationKey: "dedup-1");

        // Assert
        messageId2.Should().NotBeEmpty();
        var pending = await queueManager.GetPendingMessagesAsync();
        pending.Should().Contain(m => m.MessageId == messageId2);
    }

    [TestMethod]
    public async Task CheckoutAsync_WithReadyMessage_ReturnsTypedEnvelope()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage = new TestMessage { Id = 1, Name = "Test" };
        await queueManager.EnqueueAsync(testMessage);

        // Act
        var checkedOut = await queueManager.CheckoutAsync<TestMessage>("worker-1");

        // Assert
        checkedOut.Should().NotBeNull();
        checkedOut!.Message.Should().NotBeNull();
        checkedOut.Message!.Id.Should().Be(1);
        checkedOut.Message.Name.Should().Be("Test");
        checkedOut.Status.Should().Be(MessageStatus.InFlight);
        checkedOut.Lease.Should().NotBeNull();
    }

    [TestMethod]
    public async Task CheckoutAsync_WithNoMessages_ReturnsNull()
    {
        // Arrange
        var queueManager = CreateQueueManager();

        // Act
        var checkedOut = await queueManager.CheckoutAsync<TestMessage>("worker-1");

        // Assert
        checkedOut.Should().BeNull();
    }

    [TestMethod]
    public async Task AcknowledgeAsync_WithValidMessageId_RemovesMessage()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage = new TestMessage { Id = 1, Name = "Test" };
        await queueManager.EnqueueAsync(testMessage);
        var checkedOut = await queueManager.CheckoutAsync<TestMessage>("worker-1");

        // Act
        await queueManager.AcknowledgeAsync(checkedOut!.MessageId);

        // Assert
        var count = await queueManager.GetCountAsync();
        count.Should().Be(0);
    }

    [TestMethod]
    public async Task RequeueAsync_WithValidMessageId_RequeuesMessage()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage = new TestMessage { Id = 1, Name = "Test" };
        await queueManager.EnqueueAsync(testMessage);
        var checkedOut = await queueManager.CheckoutAsync<TestMessage>("worker-1");

        // Act
        await queueManager.RequeueAsync(checkedOut!.MessageId);

        // Assert
        var pending = await queueManager.GetPendingMessagesAsync();
        pending.Should().HaveCount(1);
        var requeuedMsg = pending.First();
        requeuedMsg.RetryCount.Should().Be(1);
        requeuedMsg.Status.Should().Be(MessageStatus.Ready);
    }

    [TestMethod]
    public async Task GetPendingMessagesAsync_ReturnsOnlyReadyMessages()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        await queueManager.EnqueueAsync(new TestMessage { Id = 1, Name = "Test1" });
        await queueManager.EnqueueAsync(new TestMessage { Id = 2, Name = "Test2" });
        await queueManager.CheckoutAsync<TestMessage>("worker-1"); // One in-flight

        // Act
        var pending = await queueManager.GetPendingMessagesAsync();

        // Assert
        pending.Should().HaveCount(1);
        pending.Should().OnlyContain(m => m.Status == MessageStatus.Ready);
    }

    [TestMethod]
    public async Task GetMessageAsync_WithValidId_ReturnsMessage()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage = new TestMessage { Id = 1, Name = "Test" };
        var messageId = await queueManager.EnqueueAsync(testMessage);

        // Act
        var message = await queueManager.GetMessageAsync(messageId);

        // Assert
        message.Should().NotBeNull();
        message!.MessageId.Should().Be(messageId);
    }

    [TestMethod]
    public async Task GetMessageAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var queueManager = CreateQueueManager();

        // Act
        var message = await queueManager.GetMessageAsync(Guid.NewGuid());

        // Assert
        message.Should().BeNull();
    }

    [TestMethod]
    public async Task EnqueueAsync_WithCorrelationId_PreservesMetadata()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage = new TestMessage { Id = 1, Name = "Test" };

        // Act
        var messageId = await queueManager.EnqueueAsync(
            testMessage,
            correlationId: "corr-123");

        // Assert
        var message = await queueManager.GetMessageAsync(messageId);
        message.Should().NotBeNull();
        message!.Metadata.CorrelationId.Should().Be("corr-123");
    }

    [TestMethod]
    public async Task RequeueAsync_WhenExceedingMaxRetries_ThrowsException()
    {
        // Arrange
        var queueManager = CreateQueueManager();
        var testMessage = new TestMessage { Id = 1, Name = "Test" };
        await queueManager.EnqueueAsync(testMessage);

        // Checkout and requeue 5 times (DefaultMaxRetries from options)
        for (int i = 0; i < 5; i++)
        {
            var checkedOut = await queueManager.CheckoutAsync<TestMessage>("worker-1");
            await queueManager.RequeueAsync(checkedOut!.MessageId);
        }

        var finalCheckout = await queueManager.CheckoutAsync<TestMessage>("worker-1");

        // Act & Assert
        Func<Task> act = async () => await queueManager.RequeueAsync(finalCheckout!.MessageId);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeded max retries*");
    }

    private static QueueManager CreateQueueManager()
    {
        var buffer = new CircularBuffer(100);
        var deduplicationIndex = new DeduplicationIndex();
        var options = new QueueOptions
        {
            Capacity = 100,
            DefaultTimeout = TimeSpan.FromMinutes(5)
        };

        return new QueueManager(buffer, deduplicationIndex, options);
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
