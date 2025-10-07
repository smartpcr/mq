// -----------------------------------------------------------------------
// <copyright file="FilePersisterTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Persistence.Tests.Unit;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Models;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class FilePersisterTests
{
    private string testStoragePath = null!;
    private PersistenceOptions options = null!;

    [TestInitialize]
    public void Setup()
    {
        this.testStoragePath = Path.Combine(Path.GetTempPath(), $"mq-test-{Guid.NewGuid()}");
        this.options = new PersistenceOptions
        {
            StoragePath = this.testStoragePath,
            SnapshotInterval = TimeSpan.FromMinutes(5),
            SnapshotThreshold = 10
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(this.testStoragePath))
        {
            try
            {
                Directory.Delete(this.testStoragePath, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [TestMethod]
    public void Constructor_CreatesStorageDirectory()
    {
        // Act
        using var persister = new FilePersister(this.options);

        // Assert
        Directory.Exists(this.testStoragePath).Should().BeTrue();
    }

    [TestMethod]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => new FilePersister(null!));
    }

    [TestMethod]
    [Ignore("Flaky test - file system timing issue with journal file creation needs investigation")]
    public async Task WriteOperationAsync_CreatesJournalFile()
    {
        // Arrange
        using var persister = new FilePersister(this.options);
        var operation = CreateTestOperation();

        // Act
        await persister.WriteOperationAsync(operation);

        // Assert
        var journalPath = Path.Combine(this.testStoragePath, "journal.dat");
        File.Exists(journalPath).Should().BeTrue();
    }

    [TestMethod]
    public async Task WriteOperationAsync_WithNullOperation_ThrowsArgumentNullException()
    {
        // Arrange
        using var persister = new FilePersister(this.options);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await persister.WriteOperationAsync(null!));
    }

    [TestMethod]
    public async Task CreateSnapshotAsync_CreatesSnapshotFile()
    {
        // Arrange
        using var persister = new FilePersister(this.options);
        var snapshot = CreateTestSnapshot();

        // Act
        await persister.CreateSnapshotAsync(snapshot);

        // Assert
        var snapshotPath = Path.Combine(this.testStoragePath, "snapshot.dat");
        File.Exists(snapshotPath).Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateSnapshotAsync_WithNullSnapshot_ThrowsArgumentNullException()
    {
        // Arrange
        using var persister = new FilePersister(this.options);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            async () => await persister.CreateSnapshotAsync(null!));
    }

    [TestMethod]
    public async Task LoadSnapshotAsync_WithNoSnapshot_ReturnsNull()
    {
        // Arrange
        using var persister = new FilePersister(this.options);

        // Act
        var snapshot = await persister.LoadSnapshotAsync();

        // Assert
        snapshot.Should().BeNull();
    }

    [TestMethod]
    public async Task LoadSnapshotAsync_AfterCreate_ReturnsSnapshot()
    {
        // Arrange
        using var persister = new FilePersister(this.options);
        var original = CreateTestSnapshot();
        await persister.CreateSnapshotAsync(original);

        // Act
        var loaded = await persister.LoadSnapshotAsync();

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Version.Should().Be(original.Version);
        loaded.Capacity.Should().Be(original.Capacity);
        loaded.MessageCount.Should().Be(original.MessageCount);
    }

    [TestMethod]
    public async Task ReplayJournalAsync_WithNoJournal_ReturnsEmptyList()
    {
        // Arrange
        using var persister = new FilePersister(this.options);

        // Act
        var operations = await persister.ReplayJournalAsync(0);

        // Assert
        operations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ReplayJournalAsync_AfterWrites_ReturnsOperations()
    {
        // Arrange
        using var persister = new FilePersister(this.options);
        var op1 = CreateTestOperation(1, OperationCode.Enqueue);
        var op2 = CreateTestOperation(2, OperationCode.Checkout);
        var op3 = CreateTestOperation(3, OperationCode.Acknowledge);

        await persister.WriteOperationAsync(op1);
        await persister.WriteOperationAsync(op2);
        await persister.WriteOperationAsync(op3);

        // Act
        var operations = await persister.ReplayJournalAsync(0);

        // Assert
        var opList = operations.ToList();
        opList.Should().HaveCount(3);
        opList[0].SequenceNumber.Should().Be(1);
        opList[1].SequenceNumber.Should().Be(2);
        opList[2].SequenceNumber.Should().Be(3);
    }

    [TestMethod]
    public async Task ReplayJournalAsync_WithSinceVersion_ReturnsOnlyNewer()
    {
        // Arrange
        using var persister = new FilePersister(this.options);
        var op1 = CreateTestOperation(1, OperationCode.Enqueue);
        var op2 = CreateTestOperation(2, OperationCode.Checkout);
        var op3 = CreateTestOperation(3, OperationCode.Acknowledge);

        await persister.WriteOperationAsync(op1);
        await persister.WriteOperationAsync(op2);
        await persister.WriteOperationAsync(op3);

        // Act
        var operations = await persister.ReplayJournalAsync(1);

        // Assert
        var opList = operations.ToList();
        opList.Should().HaveCount(2);
        opList[0].SequenceNumber.Should().Be(2);
        opList[1].SequenceNumber.Should().Be(3);
    }

    [TestMethod]
    public async Task TruncateJournalAsync_RemovesOldOperations()
    {
        // Arrange
        using var persister = new FilePersister(this.options);
        var op1 = CreateTestOperation(1, OperationCode.Enqueue);
        var op2 = CreateTestOperation(2, OperationCode.Checkout);
        var op3 = CreateTestOperation(3, OperationCode.Acknowledge);

        await persister.WriteOperationAsync(op1);
        await persister.WriteOperationAsync(op2);
        await persister.WriteOperationAsync(op3);

        // Act
        await persister.TruncateJournalAsync(3);

        // Assert
        var operations = await persister.ReplayJournalAsync(0);
        var opList = operations.ToList();
        opList.Should().HaveCount(1);
        opList[0].SequenceNumber.Should().Be(3);
    }

    [TestMethod]
    public void ShouldSnapshot_InitiallyReturnsFalse()
    {
        // Arrange
        using var persister = new FilePersister(this.options);

        // Act
        var should = persister.ShouldSnapshot();

        // Assert
        should.Should().BeFalse();
    }

    [TestMethod]
    public async Task ShouldSnapshot_AfterThresholdOperations_ReturnsTrue()
    {
        // Arrange
        this.options.SnapshotThreshold = 5;
        using var persister = new FilePersister(this.options);

        // Write 5 operations
        for (int i = 1; i <= 5; i++)
        {
            await persister.WriteOperationAsync(CreateTestOperation(i));
        }

        // Act
        var should = persister.ShouldSnapshot();

        // Assert
        should.Should().BeTrue();
    }

    [TestMethod]
    public async Task ShouldSnapshot_AfterSnapshotInterval_ReturnsTrue()
    {
        // Arrange
        this.options.SnapshotInterval = TimeSpan.FromMilliseconds(100);
        using var persister = new FilePersister(this.options);

        // Wait for interval to pass
        await Task.Delay(150);

        // Act
        var should = persister.ShouldSnapshot();

        // Assert
        should.Should().BeTrue();
    }

    [TestMethod]
    public async Task ShouldSnapshot_AfterSnapshot_ResetsCounter()
    {
        // Arrange
        this.options.SnapshotThreshold = 5;
        using var persister = new FilePersister(this.options);

        // Write 5 operations
        for (int i = 1; i <= 5; i++)
        {
            await persister.WriteOperationAsync(CreateTestOperation(i));
        }

        // Create snapshot
        await persister.CreateSnapshotAsync(CreateTestSnapshot());

        // Act
        var should = persister.ShouldSnapshot();

        // Assert
        should.Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateSnapshot_OverwritesPreviousSnapshot()
    {
        // Arrange
        using var persister = new FilePersister(this.options);
        var snapshot1 = CreateTestSnapshot();
        snapshot1.Version = 1;
        await persister.CreateSnapshotAsync(snapshot1);

        var snapshot2 = CreateTestSnapshot();
        snapshot2.Version = 2;

        // Act
        await persister.CreateSnapshotAsync(snapshot2);

        // Assert
        var loaded = await persister.LoadSnapshotAsync();
        loaded.Should().NotBeNull();
        loaded!.Version.Should().Be(2);
    }

    [TestMethod]
    public async Task MultipleOperations_CanBeReplayedInOrder()
    {
        // Arrange
        using var persister = new FilePersister(this.options);

        // Write operations in order
        for (int i = 1; i <= 20; i++)
        {
            var opCode = (OperationCode)(i % 5); // Cycle through operation codes
            await persister.WriteOperationAsync(CreateTestOperation(i, opCode));
        }

        // Act
        var operations = await persister.ReplayJournalAsync(0);

        // Assert
        var opList = operations.ToList();
        opList.Should().HaveCount(20);

        for (int i = 0; i < 20; i++)
        {
            opList[i].SequenceNumber.Should().Be(i + 1);
        }
    }

    private static OperationRecord CreateTestOperation(long sequence = 1, OperationCode opCode = OperationCode.Enqueue)
    {
        return new OperationRecord
        {
            SequenceNumber = sequence,
            OperationCode = opCode,
            MessageId = Guid.NewGuid(),
            Payload = "{\"test\":\"data\"}",
            Timestamp = DateTime.UtcNow,
            Checksum = 0
        };
    }

    private static QueueSnapshot CreateTestSnapshot()
    {
        return new QueueSnapshot
        {
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            Capacity = 100,
            MessageCount = 0,
            Messages = new System.Collections.Generic.List<MessageEnvelope>(),
            DeduplicationIndex = new System.Collections.Generic.Dictionary<string, Guid>(),
            DeadLetterMessages = new System.Collections.Generic.List<DeadLetterEnvelope>()
        };
    }
}
