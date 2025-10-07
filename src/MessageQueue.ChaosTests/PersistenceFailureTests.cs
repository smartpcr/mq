// -----------------------------------------------------------------------
// <copyright file="PersistenceFailureTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using FluentAssertions;
using MessageQueue.Core;
using MessageQueue.Core.Enums;
using MessageQueue.Core.Interfaces;
using MessageQueue.Core.Models;
using MessageQueue.Core.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MessageQueue.ChaosTests;

using MessageQueue.Core.Persistence;

/// <summary>
/// Chaos tests for persistence failure scenarios.
/// </summary>
[TestClass]
public class PersistenceFailureTests
{
    [TestMethod]
    public async Task EnqueueContinuesWhenPersistenceFails()
    {
        // Arrange
        var options = new QueueOptions { Capacity = 1000, EnablePersistence = true };
        var buffer = new CircularBuffer(options.Capacity);
        var dedupIndex = new DeduplicationIndex();
        var faultyPersister = new FaultyPersister(failureRate: 0.5); // 50% failure rate

        var queueManager = new QueueManager(buffer, dedupIndex, options, faultyPersister);

        // Act - enqueue messages even with persistence failures
        var tasks = new List<Task<Guid>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" }));
        }

        // Assert - all enqueues should succeed despite persistence failures
        var results = await Task.WhenAll(tasks);
        results.Should().HaveCount(100);
        results.Should().OnlyContain(id => id != Guid.Empty);

        // Verify messages are in buffer
        var allMessages = await buffer.GetAllMessagesAsync();
        allMessages.Count().Should().Be(100);
    }

    [TestMethod]
    public async Task RecoveryHandlesCorruptedJournal()
    {
        // Arrange
        var corruptedPersister = new CorruptedJournalPersister();
        var options = new QueueOptions { Capacity = 1000 };
        var buffer = new CircularBuffer(options.Capacity);
        var dedupIndex = new DeduplicationIndex();

        var recoveryService = new RecoveryService(
            corruptedPersister,
            buffer,
            dedupIndex);

        // Act - attempt recovery with corrupted journal
        var result = await recoveryService.RecoverAsync();

        // Assert - recovery should complete but skip corrupted entries
        result.Success.Should().BeTrue();
        // Note: Corrupted records are skipped during replay
    }

    [TestMethod]
    public async Task SnapshotFailureDoesNotAffectQueueOperation()
    {
        // Arrange
        var options = new QueueOptions { Capacity = 1000, EnablePersistence = true };
        var buffer = new CircularBuffer(options.Capacity);
        var dedupIndex = new DeduplicationIndex();
        var persister = new SnapshotFailurePersister();

        var queueManager = new QueueManager(buffer, dedupIndex, options, persister);

        // Act - enqueue messages
        for (int i = 0; i < 50; i++)
        {
            await queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }

        // Trigger snapshot - this should fail but not crash
        await queueManager.CheckAndCreateSnapshotAsync();

        // Continue enqueuing
        for (int i = 50; i < 100; i++)
        {
            await queueManager.EnqueueAsync(new TestMessage { Id = i, Data = $"Message {i}" });
        }

        // Assert - all messages should be in buffer
        var allMessages = await buffer.GetAllMessagesAsync();
        allMessages.Count().Should().Be(100);
    }

    private class TestMessage
    {
        public int Id { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    private class FaultyPersister : IPersister
    {
        private readonly double failureRate;
        private readonly Random random = new Random();

        public FaultyPersister(double failureRate)
        {
            this.failureRate = failureRate;
        }

        public Task WriteOperationAsync(OperationRecord record, CancellationToken cancellationToken = default)
        {
            if (random.NextDouble() < failureRate)
            {
                throw new IOException("Simulated persistence failure");
            }
            return Task.CompletedTask;
        }

        public Task<QueueSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<QueueSnapshot?>(null);
        }

        public Task<IEnumerable<OperationRecord>> ReplayJournalAsync(long fromVersion, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Enumerable.Empty<OperationRecord>());
        }

        public Task CreateSnapshotAsync(QueueSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task TruncateJournalAsync(long beforeVersion, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public bool ShouldSnapshot() => false;
    }

    private class CorruptedJournalPersister : IPersister
    {
        public Task WriteOperationAsync(OperationRecord record, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<QueueSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<QueueSnapshot?>(null);
        }

        public Task<IEnumerable<OperationRecord>> ReplayJournalAsync(long fromVersion, CancellationToken cancellationToken = default)
        {
            // Return some valid and some corrupted records
            var records = new List<OperationRecord>
            {
                new OperationRecord
                {
                    SequenceNumber = 1,
                    OperationCode = OperationCode.Enqueue,
                    MessageId = Guid.NewGuid(),
                    Payload = "{\"Invalid JSON",
                    Timestamp = DateTime.UtcNow
                }
            };

            return Task.FromResult<IEnumerable<OperationRecord>>(records);
        }

        public Task CreateSnapshotAsync(QueueSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task TruncateJournalAsync(long beforeVersion, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public bool ShouldSnapshot() => false;
    }

    private class SnapshotFailurePersister : IPersister
    {
        public Task WriteOperationAsync(OperationRecord record, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<QueueSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<QueueSnapshot?>(null);
        }

        public Task<IEnumerable<OperationRecord>> ReplayJournalAsync(long fromVersion, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Enumerable.Empty<OperationRecord>());
        }

        public Task CreateSnapshotAsync(QueueSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            throw new IOException("Simulated snapshot failure");
        }

        public Task TruncateJournalAsync(long beforeVersion, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public bool ShouldSnapshot() => true;
    }
}
