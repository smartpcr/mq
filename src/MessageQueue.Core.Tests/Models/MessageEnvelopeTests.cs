// -----------------------------------------------------------------------
// <copyright file="MessageEnvelopeTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Tests.Models;

using System.Text.Json;
using FluentAssertions;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class MessageEnvelopeTests
{
    [TestMethod]
    public void MessageEnvelope_SerializationRoundTrip_PreservesAllFields()
    {
        // Arrange
        var envelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = "{\"id\":1,\"name\":\"test\"}",
            DeduplicationKey = "test-key-1",
            Status = MessageStatus.Ready,
            RetryCount = 0,
            MaxRetries = 5,
            EnqueuedAt = DateTime.UtcNow,
            Lease = new LeaseInfo
            {
                HandlerId = "worker-1",
                CheckoutTimestamp = DateTime.UtcNow,
                LeaseExpiry = DateTime.UtcNow.AddMinutes(5),
                ExtensionCount = 0
            },
            LastPersistedVersion = 100,
            Metadata = new MessageMetadata
            {
                CorrelationId = "corr-123",
                Headers = new Dictionary<string, string> { { "key1", "value1" } },
                Source = "TestSource",
                Version = 1
            }
        };

        // Act
        var json = JsonSerializer.Serialize(envelope);
        var deserialized = JsonSerializer.Deserialize<MessageEnvelope>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.MessageId.Should().Be(envelope.MessageId);
        deserialized.MessageType.Should().Be(envelope.MessageType);
        deserialized.Payload.Should().Be(envelope.Payload);
        deserialized.DeduplicationKey.Should().Be(envelope.DeduplicationKey);
        deserialized.Status.Should().Be(envelope.Status);
        deserialized.RetryCount.Should().Be(envelope.RetryCount);
        deserialized.MaxRetries.Should().Be(envelope.MaxRetries);
        deserialized.Lease.Should().NotBeNull();
        deserialized.Lease!.HandlerId.Should().Be("worker-1");
        deserialized.Metadata.CorrelationId.Should().Be("corr-123");
    }

    [TestMethod]
    public void MessageEnvelope_StatusTransitions_UpdateCorrectly()
    {
        // Arrange
        var envelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = "{}",
            Status = MessageStatus.Ready
        };

        // Act & Assert - Ready to InFlight
        envelope.Status = MessageStatus.InFlight;
        envelope.Status.Should().Be(MessageStatus.InFlight);

        // InFlight to Completed
        envelope.Status = MessageStatus.Completed;
        envelope.Status.Should().Be(MessageStatus.Completed);
    }

    [TestMethod]
    public void MessageEnvelope_RetryCount_IncrementsCorrectly()
    {
        // Arrange
        var envelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = "{}",
            RetryCount = 0,
            MaxRetries = 3
        };

        // Act
        envelope.RetryCount++;
        envelope.RetryCount++;

        // Assert
        envelope.RetryCount.Should().Be(2);
        envelope.RetryCount.Should().BeLessThan(envelope.MaxRetries);
    }
}
