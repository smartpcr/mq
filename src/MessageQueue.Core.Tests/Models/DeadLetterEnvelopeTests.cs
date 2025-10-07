namespace MessageQueue.Core.Tests.Models;

using System.Text.Json;
using FluentAssertions;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class DeadLetterEnvelopeTests
{
    [TestMethod]
    public void DeadLetterEnvelope_FromMessageEnvelope_CopiesAllFields()
    {
        // Arrange
        var originalEnvelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = "{\"test\":\"data\"}",
            DeduplicationKey = "test-key",
            RetryCount = 3,
            MaxRetries = 3,
            Lease = new LeaseInfo { HandlerId = "worker-1" }
        };

        var exception = new InvalidOperationException("Test error");

        // Act
        var dlqEnvelope = DeadLetterEnvelope.FromMessageEnvelope(
            originalEnvelope,
            "Max retries exceeded",
            exception
        );

        // Assert
        dlqEnvelope.MessageId.Should().Be(originalEnvelope.MessageId);
        dlqEnvelope.MessageType.Should().Be(originalEnvelope.MessageType);
        dlqEnvelope.Payload.Should().Be(originalEnvelope.Payload);
        dlqEnvelope.Status.Should().Be(MessageStatus.DeadLetter);
        dlqEnvelope.FailureReason.Should().Be("Max retries exceeded");
        dlqEnvelope.ExceptionMessage.Should().Be("Test error");
        dlqEnvelope.ExceptionType.Should().Contain("InvalidOperationException");
        dlqEnvelope.LastHandlerId.Should().Be("worker-1");
        dlqEnvelope.FailureTimestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void DeadLetterEnvelope_SerializationRoundTrip_PreservesAllFields()
    {
        // Arrange
        var dlqEnvelope = new DeadLetterEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = "{}",
            FailureReason = "Processing failed",
            ExceptionMessage = "Null reference",
            ExceptionStackTrace = "at MyClass.MyMethod()",
            ExceptionType = "System.NullReferenceException",
            FailureTimestamp = DateTime.UtcNow,
            LastHandlerId = "worker-2"
        };

        // Act
        var json = JsonSerializer.Serialize(dlqEnvelope);
        var deserialized = JsonSerializer.Deserialize<DeadLetterEnvelope>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.FailureReason.Should().Be("Processing failed");
        deserialized.ExceptionMessage.Should().Be("Null reference");
        deserialized.LastHandlerId.Should().Be("worker-2");
    }
}
