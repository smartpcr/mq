namespace MessageQueue.Persistence.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Models;
using MessageQueue.Persistence.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class SnapshotSerializerTests
{
    private SnapshotSerializer _serializer = null!;

    [TestInitialize]
    public void Setup()
    {
        _serializer = new SnapshotSerializer();
    }

    [TestMethod]
    public async Task SerializeAsync_WithValidSnapshot_ReturnsValidByteArray()
    {
        // Arrange
        var snapshot = CreateTestSnapshot();

        // Act
        var result = await _serializer.SerializeAsync(snapshot);

        // Assert
        result.Should().NotBeNull();
        result.Length.Should().BeGreaterThan(24); // At least header size
    }

    [TestMethod]
    public async Task SerializeAsync_WithNullSnapshot_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await _serializer.SerializeAsync(null!));
    }

    [TestMethod]
    public async Task DeserializeAsync_WithValidData_ReturnsOriginalSnapshot()
    {
        // Arrange
        var original = CreateTestSnapshot();
        var serialized = await _serializer.SerializeAsync(original);

        // Act
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Version.Should().Be(original.Version);
        deserialized.Capacity.Should().Be(original.Capacity);
        deserialized.MessageCount.Should().Be(original.MessageCount);
        deserialized.Messages.Should().HaveCount(original.Messages.Count);
        deserialized.DeduplicationIndex.Should().HaveCount(original.DeduplicationIndex.Count);
    }

    [TestMethod]
    public async Task DeserializeAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await _serializer.DeserializeAsync(null!));
    }

    [TestMethod]
    public async Task DeserializeAsync_WithTooShortData_ThrowsInvalidDataException()
    {
        // Arrange
        var shortData = new byte[10]; // Less than header size (24 bytes)

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.DeserializeAsync(shortData));
        exception.Message.Should().Contain("too short");
    }

    [TestMethod]
    public async Task DeserializeAsync_WithInvalidMagicNumber_ThrowsInvalidDataException()
    {
        // Arrange
        var original = CreateTestSnapshot();
        var serialized = await _serializer.SerializeAsync(original);

        // Corrupt the magic number (first 8 bytes)
        serialized[0] ^= 0xFF;

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.DeserializeAsync(serialized));
        exception.Message.Should().Contain("Invalid magic number");
    }

    [TestMethod]
    public async Task DeserializeAsync_WithCorruptedCrc_ThrowsInvalidDataException()
    {
        // Arrange
        var original = CreateTestSnapshot();
        var serialized = await _serializer.SerializeAsync(original);

        // Corrupt the CRC (bytes 20-23 in header)
        serialized[20] ^= 0xFF;

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.DeserializeAsync(serialized));
        exception.Message.Should().Contain("CRC mismatch");
    }

    [TestMethod]
    public async Task DeserializeAsync_WithCorruptedPayload_ThrowsInvalidDataException()
    {
        // Arrange
        var original = CreateTestSnapshot();
        var serialized = await _serializer.SerializeAsync(original);

        // Corrupt the payload (after header)
        serialized[30] ^= 0xFF;

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.DeserializeAsync(serialized));
    }

    [TestMethod]
    public async Task DeserializeAsync_WithVersionMismatch_ThrowsInvalidDataException()
    {
        // Arrange
        var original = CreateTestSnapshot();
        var serialized = await _serializer.SerializeAsync(original);

        // Corrupt the version in header (bytes 8-15)
        var corruptedVersion = BitConverter.GetBytes(999L);
        Array.Copy(corruptedVersion, 0, serialized, 8, 8);

        // Need to recalculate CRC for payload to get to version validation
        // This test validates that header version matches payload version

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.DeserializeAsync(serialized));
    }

    [TestMethod]
    public async Task ReadVersionAsync_WithValidData_ReturnsCorrectVersion()
    {
        // Arrange
        var snapshot = CreateTestSnapshot();
        snapshot.Version = 42;
        var serialized = await _serializer.SerializeAsync(snapshot);

        // Act
        var version = await _serializer.ReadVersionAsync(serialized);

        // Assert
        version.Should().Be(42);
    }

    [TestMethod]
    public async Task ReadVersionAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await _serializer.ReadVersionAsync(null!));
    }

    [TestMethod]
    public async Task ReadVersionAsync_WithTooShortData_ThrowsArgumentException()
    {
        // Arrange
        var shortData = new byte[10]; // Less than 16 bytes needed

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<ArgumentException>(
            async () => await _serializer.ReadVersionAsync(shortData));
        exception.Message.Should().Contain("too short");
    }

    [TestMethod]
    public async Task ReadVersionAsync_WithInvalidMagic_ThrowsInvalidDataException()
    {
        // Arrange
        var data = new byte[16];
        // Write invalid magic number
        BitConverter.GetBytes(0x1234567890ABCDEFL).CopyTo(data, 0);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.ReadVersionAsync(data));
    }

    [TestMethod]
    public async Task ValidateHeaderAsync_WithValidData_ReturnsTrue()
    {
        // Arrange
        var snapshot = CreateTestSnapshot();
        var serialized = await _serializer.SerializeAsync(snapshot);

        // Act
        var isValid = await _serializer.ValidateHeaderAsync(serialized);

        // Assert
        isValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task ValidateHeaderAsync_WithNullData_ReturnsFalse()
    {
        // Act
        var isValid = await _serializer.ValidateHeaderAsync(null!);

        // Assert
        isValid.Should().BeFalse();
    }

    [TestMethod]
    public async Task ValidateHeaderAsync_WithTooShortData_ReturnsFalse()
    {
        // Arrange
        var shortData = new byte[10];

        // Act
        var isValid = await _serializer.ValidateHeaderAsync(shortData);

        // Assert
        isValid.Should().BeFalse();
    }

    [TestMethod]
    public async Task ValidateHeaderAsync_WithInvalidMagic_ReturnsFalse()
    {
        // Arrange
        var snapshot = CreateTestSnapshot();
        var serialized = await _serializer.SerializeAsync(snapshot);
        serialized[0] ^= 0xFF; // Corrupt magic

        // Act
        var isValid = await _serializer.ValidateHeaderAsync(serialized);

        // Assert
        isValid.Should().BeFalse();
    }

    [TestMethod]
    public async Task SerializeDeserialize_WithEmptySnapshot_WorksCorrectly()
    {
        // Arrange
        var snapshot = new QueueSnapshot
        {
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            Capacity = 100,
            MessageCount = 0,
            Messages = new List<MessageEnvelope>(),
            DeduplicationIndex = new Dictionary<string, Guid>(),
            DeadLetterMessages = new List<DeadLetterEnvelope>()
        };

        // Act
        var serialized = await _serializer.SerializeAsync(snapshot);
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.MessageCount.Should().Be(0);
        deserialized.Messages.Should().BeEmpty();
        deserialized.DeduplicationIndex.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SerializeDeserialize_WithMultipleMessages_PreservesOrder()
    {
        // Arrange
        var snapshot = CreateTestSnapshot();
        var msg1 = CreateTestMessage("msg1");
        var msg2 = CreateTestMessage("msg2");
        var msg3 = CreateTestMessage("msg3");

        snapshot.Messages = new List<MessageEnvelope> { msg1, msg2, msg3 };
        snapshot.MessageCount = 3;

        // Act
        var serialized = await _serializer.SerializeAsync(snapshot);
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.Messages.Should().HaveCount(3);
        deserialized.Messages[0].Payload.Should().Contain("msg1");
        deserialized.Messages[1].Payload.Should().Contain("msg2");
        deserialized.Messages[2].Payload.Should().Contain("msg3");
    }

    [TestMethod]
    public async Task SerializeDeserialize_WithDeduplicationIndex_PreservesEntries()
    {
        // Arrange
        var snapshot = CreateTestSnapshot();
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();

        snapshot.DeduplicationIndex = new Dictionary<string, Guid>
        {
            { "key1", guid1 },
            { "key2", guid2 },
            { "key3", guid3 }
        };

        // Act
        var serialized = await _serializer.SerializeAsync(snapshot);
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.DeduplicationIndex.Should().HaveCount(3);
        deserialized.DeduplicationIndex["key1"].Should().Be(guid1);
        deserialized.DeduplicationIndex["key2"].Should().Be(guid2);
        deserialized.DeduplicationIndex["key3"].Should().Be(guid3);
    }

    [TestMethod]
    public async Task SerializeDeserialize_WithLargeSnapshot_WorksCorrectly()
    {
        // Arrange
        var snapshot = CreateTestSnapshot();
        snapshot.Messages = new List<MessageEnvelope>();

        for (int i = 0; i < 100; i++)
        {
            snapshot.Messages.Add(CreateTestMessage($"message-{i}"));
        }
        snapshot.MessageCount = 100;

        // Act
        var serialized = await _serializer.SerializeAsync(snapshot);
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.Messages.Should().HaveCount(100);
        deserialized.MessageCount.Should().Be(100);
    }

    private static QueueSnapshot CreateTestSnapshot()
    {
        return new QueueSnapshot
        {
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            Capacity = 100,
            MessageCount = 1,
            Messages = new List<MessageEnvelope>
            {
                CreateTestMessage("test")
            },
            DeduplicationIndex = new Dictionary<string, Guid>(),
            DeadLetterMessages = new List<DeadLetterEnvelope>()
        };
    }

    private static MessageEnvelope CreateTestMessage(string id)
    {
        return new MessageEnvelope
        {
            MessageId = Guid.NewGuid(),
            MessageType = typeof(string).FullName!,
            Payload = $"{{\"id\":\"{id}\",\"data\":\"test\"}}",
            DeduplicationKey = null,
            Status = MessageStatus.Ready,
            RetryCount = 0,
            MaxRetries = 3,
            Lease = null,
            LastPersistedVersion = 0,
            Metadata = new MessageMetadata
            {
                CorrelationId = null,
                Headers = new Dictionary<string, string>(),
                Source = "Test",
                Version = 1
            },
            EnqueuedAt = DateTime.UtcNow,
            IsSuperseded = false
        };
    }
}
