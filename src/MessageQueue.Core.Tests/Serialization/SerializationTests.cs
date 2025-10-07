// -----------------------------------------------------------------------
// <copyright file="SerializationTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Tests.Serialization;

using System.Text.Json;
using FluentAssertions;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class SerializationTests
{
    [TestMethod]
    public void OperationRecord_Serialization_HandlesAllOperationCodes()
    {
        // Test each operation code
        foreach (OperationCode opCode in Enum.GetValues(typeof(OperationCode)))
        {
            // Arrange
            var record = new OperationRecord
            {
                SequenceNumber = 123,
                OperationCode = opCode,
                MessageId = Guid.NewGuid(),
                Payload = "{}",
                Timestamp = DateTime.UtcNow,
                Checksum = 12345
            };

            // Act
            var json = JsonSerializer.Serialize(record);
            var deserialized = JsonSerializer.Deserialize<OperationRecord>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.OperationCode.Should().Be(opCode);
            deserialized.SequenceNumber.Should().Be(123);
        }
    }

    [TestMethod]
    public void QueueSnapshot_Serialization_PreservesDeduplicationIndex()
    {
        // Arrange
        var snapshot = new QueueSnapshot
        {
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            Capacity = 10000,
            MessageCount = 2,
            Messages = new List<MessageEnvelope>
            {
                new() { MessageId = Guid.NewGuid(), MessageType = "Type1", Payload = "{}" },
                new() { MessageId = Guid.NewGuid(), MessageType = "Type2", Payload = "{}" }
            },
            DeduplicationIndex = new Dictionary<string, Guid>
            {
                { "key1", Guid.NewGuid() },
                { "key2", Guid.NewGuid() }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<QueueSnapshot>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.DeduplicationIndex.Should().HaveCount(2);
        deserialized.DeduplicationIndex.Should().ContainKey("key1");
        deserialized.DeduplicationIndex.Should().ContainKey("key2");
        deserialized.Messages.Should().HaveCount(2);
    }

    [TestMethod]
    public void MessageStatus_Enum_SerializesAsString()
    {
        // Arrange
        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        var envelope = new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = "Test",
            Payload = "{}",
            Status = MessageStatus.InFlight
        };

        // Act
        var json = JsonSerializer.Serialize(envelope, options);

        // Assert
        json.Should().Contain("\"InFlight\"");
    }
}
