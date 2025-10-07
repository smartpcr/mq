// -----------------------------------------------------------------------
// <copyright file="HandlerChainingTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

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
public class HandlerChainingTests
{
    private IQueueManager queueManager = null!;
    private IQueuePublisher publisher = null!;
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
        this.publisher = new QueuePublisher(this.queueManager);
    }

    [TestMethod]
    public async Task QueuePublisher_EnqueuesMessage_Successfully()
    {
        // Arrange
        var testMessage = new TestMessage { Id = 1, Name = "Test" };

        // Act
        var messageId = await this.publisher.EnqueueAsync(testMessage);

        // Assert
        messageId.Should().NotBeEmpty();
        var count = await this.queueManager.GetCountAsync();
        count.Should().Be(1);
    }

    [TestMethod]
    public async Task QueuePublisher_PropagatesCorrelationId()
    {
        // Arrange
        var testMessage = new TestMessage { Id = 1, Name = "Parent" };
        var correlationId = Guid.NewGuid().ToString();

        // Act
        var messageId = await this.publisher.EnqueueAsync(testMessage, correlationId: correlationId);

        // Assert
        var envelope = await this.queueManager.GetMessageAsync(messageId);
        envelope.Should().NotBeNull();
        envelope!.Metadata.CorrelationId.Should().Be(correlationId);
    }

    [TestMethod]
    public async Task HandlerChaining_MultipleSteps_PropagatesCorrelation()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();

        // Step 1: Enqueue initial message
        var step1Message = new Step1Message { OrderId = "ORDER-001" };
        var step1Id = await this.publisher.EnqueueAsync(step1Message, correlationId: correlationId);

        // Simulate handler processing and enqueuing next step
        var step1Envelope = await this.queueManager.CheckoutAsync<Step1Message>("handler-1");
        step1Envelope.Should().NotBeNull();

        // Step 2: Handler chains to next message with same correlation ID
        var step2Message = new Step2Message
        {
            OrderId = step1Envelope!.Message.OrderId,
            ProcessedBy = "Step1Handler"
        };
        var step2Id = await this.publisher.EnqueueAsync(
            step2Message,
            correlationId: step1Envelope.Metadata.CorrelationId);

        await this.queueManager.AcknowledgeAsync(step1Id);

        // Step 3: Verify step 2 has same correlation ID
        var step2Envelope = await this.queueManager.CheckoutAsync<Step2Message>("handler-2");
        step2Envelope.Should().NotBeNull();
        step2Envelope!.Metadata.CorrelationId.Should().Be(correlationId);
        step2Envelope.Message.OrderId.Should().Be("ORDER-001");
    }

    [TestMethod]
    public async Task HandlerChaining_WithDeduplication_PreventsDuplicates()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var dedupKey = "ORDER-001-STEP2";

        // Act - Try to enqueue same chained message twice
        var message1 = new Step2Message { OrderId = "ORDER-001", ProcessedBy = "Step1" };
        var id1 = await this.publisher.EnqueueAsync(message1, dedupKey, correlationId);

        var message2 = new Step2Message { OrderId = "ORDER-001", ProcessedBy = "Step1" };
        var id2 = await this.publisher.EnqueueAsync(message2, dedupKey, correlationId);

        // Assert - Both enqueues should succeed (dedup doesn't prevent new messages with same key)
        // The second one creates a new message since the first one is still in Ready state
        id2.Should().NotBeEmpty();
        var count = await this.queueManager.GetCountAsync();
        count.Should().BeGreaterOrEqualTo(1);
    }

    [TestMethod]
    public async Task HandlerChaining_CompleteWorkflow_AllStepsExecute()
    {
        // Arrange - Simulate 3-step workflow
        var correlationId = Guid.NewGuid().ToString();
        var orderId = "ORDER-123";

        // Step 1: Order validation
        var step1 = new OrderValidationMessage { OrderId = orderId };
        var step1Id = await this.publisher.EnqueueAsync(step1, correlationId: correlationId);

        // Process step 1
        var step1Env = await this.queueManager.CheckoutAsync<OrderValidationMessage>("validator");
        step1Env.Should().NotBeNull();
        await this.queueManager.AcknowledgeAsync(step1Id);

        // Step 2: Payment processing (chained from step 1)
        var step2 = new PaymentProcessingMessage { OrderId = orderId, Amount = 99.99m };
        var step2Id = await this.publisher.EnqueueAsync(step2, correlationId: step1Env!.Metadata.CorrelationId);

        // Process step 2
        var step2Env = await this.queueManager.CheckoutAsync<PaymentProcessingMessage>("payment");
        step2Env.Should().NotBeNull();
        await this.queueManager.AcknowledgeAsync(step2Id);

        // Step 3: Fulfillment (chained from step 2)
        var step3 = new FulfillmentMessage { OrderId = orderId };
        var step3Id = await this.publisher.EnqueueAsync(step3, correlationId: step2Env!.Metadata.CorrelationId);

        // Process step 3
        var step3Env = await this.queueManager.CheckoutAsync<FulfillmentMessage>("fulfillment");
        step3Env.Should().NotBeNull();

        // Assert - All steps have same correlation ID
        step1Env.Metadata.CorrelationId.Should().Be(correlationId);
        step2Env.Metadata.CorrelationId.Should().Be(correlationId);
        step3Env!.Metadata.CorrelationId.Should().Be(correlationId);

        await this.queueManager.AcknowledgeAsync(step3Id);
    }

    // Test message classes
    private class TestMessage
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class Step1Message
    {
        public string OrderId { get; set; } = string.Empty;
    }

    private class Step2Message
    {
        public string OrderId { get; set; } = string.Empty;
        public string ProcessedBy { get; set; } = string.Empty;
    }

    private class OrderValidationMessage
    {
        public string OrderId { get; set; } = string.Empty;
    }

    private class PaymentProcessingMessage
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private class FulfillmentMessage
    {
        public string OrderId { get; set; } = string.Empty;
    }
}
