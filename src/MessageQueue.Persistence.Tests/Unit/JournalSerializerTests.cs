namespace MessageQueue.Persistence.Tests.Unit;

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Models;
using MessageQueue.Persistence.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class JournalSerializerTests
{
    private JournalSerializer _serializer = null!;

    [TestInitialize]
    public void Setup()
    {
        _serializer = new JournalSerializer();
    }

    [TestMethod]
    public async Task SerializeAsync_WithValidRecord_ReturnsValidByteArray()
    {
        // Arrange
        var record = CreateTestRecord();

        // Act
        var result = await _serializer.SerializeAsync(record);

        // Assert
        result.Should().NotBeNull();
        result.Length.Should().BeGreaterThan(16); // At least header size
    }

    [TestMethod]
    public async Task SerializeAsync_WithNullRecord_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await _serializer.SerializeAsync(null!));
    }

    [TestMethod]
    public async Task DeserializeAsync_WithValidData_ReturnsOriginalRecord()
    {
        // Arrange
        var original = CreateTestRecord();
        var serialized = await _serializer.SerializeAsync(original);

        // Act
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.SequenceNumber.Should().Be(original.SequenceNumber);
        deserialized.OperationCode.Should().Be(original.OperationCode);
        deserialized.MessageId.Should().Be(original.MessageId);
        deserialized.Payload.Should().Be(original.Payload);
        deserialized.Timestamp.Should().BeCloseTo(original.Timestamp, TimeSpan.FromMilliseconds(1));
    }

    [TestMethod]
    public async Task DeserializeAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await _serializer.DeserializeAsync(null!));
    }

    [TestMethod]
    public async Task DeserializeAsync_WithTooShortData_ThrowsArgumentException()
    {
        // Arrange
        var shortData = new byte[10]; // Less than header size (16 bytes)

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<ArgumentException>(
            async () => await _serializer.DeserializeAsync(shortData));
        exception.Message.Should().Contain("too short");
    }

    [TestMethod]
    public async Task DeserializeAsync_WithCorruptedCrc_ThrowsInvalidDataException()
    {
        // Arrange
        var original = CreateTestRecord();
        var serialized = await _serializer.SerializeAsync(original);

        // Corrupt the CRC (bytes 12-15 in header)
        serialized[12] ^= 0xFF;

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.DeserializeAsync(serialized));
        exception.Message.Should().Contain("CRC mismatch");
    }

    [TestMethod]
    public async Task DeserializeAsync_WithCorruptedPayload_ThrowsInvalidDataException()
    {
        // Arrange
        var original = CreateTestRecord();
        var serialized = await _serializer.SerializeAsync(original);

        // Corrupt the payload (after header)
        serialized[20] ^= 0xFF;

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.DeserializeAsync(serialized));
    }

    [TestMethod]
    public async Task DeserializeAsync_WithInvalidPayloadLength_ThrowsInvalidDataException()
    {
        // Arrange
        var original = CreateTestRecord();
        var serialized = await _serializer.SerializeAsync(original);

        // Corrupt the payload length (bytes 8-11 in header) to be larger than actual data
        var corruptedLength = BitConverter.GetBytes(serialized.Length * 2);
        Array.Copy(corruptedLength, 0, serialized, 8, 4);

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(
            async () => await _serializer.DeserializeAsync(serialized));
        exception.Message.Should().Contain("does not match payload length");
    }

    [TestMethod]
    public async Task ReadSequenceNumberAsync_WithValidData_ReturnsCorrectSequenceNumber()
    {
        // Arrange
        var record = CreateTestRecord();
        record.SequenceNumber = 42;
        var serialized = await _serializer.SerializeAsync(record);

        // Act
        var sequenceNumber = await _serializer.ReadSequenceNumberAsync(serialized);

        // Assert
        sequenceNumber.Should().Be(42);
    }

    [TestMethod]
    public async Task ReadSequenceNumberAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await _serializer.ReadSequenceNumberAsync(null!));
    }

    [TestMethod]
    public async Task ReadSequenceNumberAsync_WithTooShortData_ThrowsArgumentException()
    {
        // Arrange
        var shortData = new byte[4]; // Less than 8 bytes needed for sequence number

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<ArgumentException>(
            async () => await _serializer.ReadSequenceNumberAsync(shortData));
        exception.Message.Should().Contain("too short");
    }

    [TestMethod]
    public async Task ReadRecordSizeAsync_WithValidData_ReturnsCorrectSize()
    {
        // Arrange
        var record = CreateTestRecord();
        var serialized = await _serializer.SerializeAsync(record);

        // Act
        var recordSize = await _serializer.ReadRecordSizeAsync(serialized);

        // Assert
        recordSize.Should().Be(serialized.Length);
    }

    [TestMethod]
    public async Task ReadRecordSizeAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await _serializer.ReadRecordSizeAsync(null!));
    }

    [TestMethod]
    public async Task ReadRecordSizeAsync_WithTooShortData_ThrowsArgumentException()
    {
        // Arrange
        var shortData = new byte[10]; // Less than header size (16 bytes)

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<ArgumentException>(
            async () => await _serializer.ReadRecordSizeAsync(shortData));
        exception.Message.Should().Contain("too short");
    }

    [TestMethod]
    public async Task SerializeDeserialize_WithAllOperationCodes_WorksCorrectly()
    {
        // Test all operation codes
        foreach (OperationCode opCode in Enum.GetValues(typeof(OperationCode)))
        {
            // Arrange
            var record = CreateTestRecord();
            record.OperationCode = opCode;

            // Act
            var serialized = await _serializer.SerializeAsync(record);
            var deserialized = await _serializer.DeserializeAsync(serialized);

            // Assert
            deserialized.OperationCode.Should().Be(opCode);
        }
    }

    [TestMethod]
    public async Task SerializeDeserialize_WithLargePayload_WorksCorrectly()
    {
        // Arrange
        var record = CreateTestRecord();
        record.Payload = new string('X', 10000); // Large payload

        // Act
        var serialized = await _serializer.SerializeAsync(record);
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.Payload.Should().Be(record.Payload);
        deserialized.Payload.Length.Should().Be(10000);
    }

    [TestMethod]
    public async Task SerializeDeserialize_WithEmptyPayload_WorksCorrectly()
    {
        // Arrange
        var record = CreateTestRecord();
        record.Payload = string.Empty;

        // Act
        var serialized = await _serializer.SerializeAsync(record);
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.Payload.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SerializeDeserialize_WithSpecialCharacters_WorksCorrectly()
    {
        // Arrange
        var record = CreateTestRecord();
        record.Payload = "{\"test\":\"こんにちは\",\"emoji\":\"🤖\",\"special\":\"\\n\\t\\\"\"}";

        // Act
        var serialized = await _serializer.SerializeAsync(record);
        var deserialized = await _serializer.DeserializeAsync(serialized);

        // Assert
        deserialized.Payload.Should().Be(record.Payload);
    }

    [TestMethod]
    public async Task MultipleRecords_SerializeDeserialize_MaintainIndependence()
    {
        // Arrange
        var record1 = CreateTestRecord();
        record1.SequenceNumber = 1;
        record1.OperationCode = OperationCode.Enqueue;

        var record2 = CreateTestRecord();
        record2.SequenceNumber = 2;
        record2.OperationCode = OperationCode.Checkout;

        var record3 = CreateTestRecord();
        record3.SequenceNumber = 3;
        record3.OperationCode = OperationCode.Acknowledge;

        // Act
        var serialized1 = await _serializer.SerializeAsync(record1);
        var serialized2 = await _serializer.SerializeAsync(record2);
        var serialized3 = await _serializer.SerializeAsync(record3);

        var deserialized1 = await _serializer.DeserializeAsync(serialized1);
        var deserialized2 = await _serializer.DeserializeAsync(serialized2);
        var deserialized3 = await _serializer.DeserializeAsync(serialized3);

        // Assert
        deserialized1.SequenceNumber.Should().Be(1);
        deserialized1.OperationCode.Should().Be(OperationCode.Enqueue);

        deserialized2.SequenceNumber.Should().Be(2);
        deserialized2.OperationCode.Should().Be(OperationCode.Checkout);

        deserialized3.SequenceNumber.Should().Be(3);
        deserialized3.OperationCode.Should().Be(OperationCode.Acknowledge);
    }

    private static OperationRecord CreateTestRecord()
    {
        return new OperationRecord
        {
            SequenceNumber = 1,
            OperationCode = OperationCode.Enqueue,
            MessageId = Guid.NewGuid(),
            Payload = "{\"test\":\"data\"}",
            Timestamp = DateTime.UtcNow,
            Checksum = 0
        };
    }
}
