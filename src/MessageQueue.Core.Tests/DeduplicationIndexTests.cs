// -----------------------------------------------------------------------
// <copyright file="DeduplicationIndexTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace MessageQueue.Core.Tests;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using MessageQueue.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class DeduplicationIndexTests
{
    [TestMethod]
    public async Task TryAddAsync_WithNewKey_ReturnsTrue()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var messageId = Guid.NewGuid();

        // Act
        bool added = await index.TryAddAsync("key1", messageId);

        // Assert
        added.Should().BeTrue();
        var count = await index.GetCountAsync();
        count.Should().Be(1);
    }

    [TestMethod]
    public async Task TryAddAsync_WithExistingKey_ReturnsFalse()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var messageId1 = Guid.NewGuid();
        var messageId2 = Guid.NewGuid();
        await index.TryAddAsync("key1", messageId1);

        // Act
        bool added = await index.TryAddAsync("key1", messageId2);

        // Assert
        added.Should().BeFalse();
        var count = await index.GetCountAsync();
        count.Should().Be(1);
    }

    [TestMethod]
    public async Task TryGetAsync_WithExistingKey_ReturnsMessageId()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var messageId = Guid.NewGuid();
        await index.TryAddAsync("key1", messageId);

        // Act
        var result = await index.TryGetAsync("key1");

        // Assert
        result.Should().Be(messageId);
    }

    [TestMethod]
    public async Task TryGetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var index = new DeduplicationIndex();

        // Act
        var result = await index.TryGetAsync("key1");

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task UpdateAsync_WithExistingKey_UpdatesMessageId()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var messageId1 = Guid.NewGuid();
        var messageId2 = Guid.NewGuid();
        await index.TryAddAsync("key1", messageId1);

        // Act
        bool updated = await index.UpdateAsync("key1", messageId2);

        // Assert
        updated.Should().BeTrue();
        var result = await index.TryGetAsync("key1");
        result.Should().Be(messageId2);
    }

    [TestMethod]
    public async Task RemoveAsync_WithExistingKey_RemovesEntry()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var messageId = Guid.NewGuid();
        await index.TryAddAsync("key1", messageId);

        // Act
        bool removed = await index.RemoveAsync("key1");

        // Assert
        removed.Should().BeTrue();
        var count = await index.GetCountAsync();
        count.Should().Be(0);
    }

    [TestMethod]
    public async Task RemoveAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        var index = new DeduplicationIndex();

        // Act
        bool removed = await index.RemoveAsync("key1");

        // Assert
        removed.Should().BeFalse();
    }

    [TestMethod]
    public async Task ContainsKeyAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var messageId = Guid.NewGuid();
        await index.TryAddAsync("key1", messageId);

        // Act
        bool contains = await index.ContainsKeyAsync("key1");

        // Assert
        contains.Should().BeTrue();
    }

    [TestMethod]
    public async Task ContainsKeyAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        var index = new DeduplicationIndex();

        // Act
        bool contains = await index.ContainsKeyAsync("key1");

        // Assert
        contains.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetSnapshotAsync_ReturnsAllEntries()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var messageId1 = Guid.NewGuid();
        var messageId2 = Guid.NewGuid();
        await index.TryAddAsync("key1", messageId1);
        await index.TryAddAsync("key2", messageId2);

        // Act
        var snapshot = await index.GetSnapshotAsync();

        // Assert
        snapshot.Should().HaveCount(2);
        snapshot.Should().ContainKey("key1");
        snapshot.Should().ContainKey("key2");
        snapshot["key1"].Should().Be(messageId1);
        snapshot["key2"].Should().Be(messageId2);
    }

    [TestMethod]
    public async Task RestoreFromSnapshotAsync_RestoresAllEntries()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var messageId1 = Guid.NewGuid();
        var messageId2 = Guid.NewGuid();
        var snapshot = new System.Collections.Generic.Dictionary<string, Guid>
        {
            { "key1", messageId1 },
            { "key2", messageId2 }
        };

        // Act
        await index.RestoreFromSnapshotAsync(snapshot);

        // Assert
        var count = await index.GetCountAsync();
        count.Should().Be(2);
        var result1 = await index.TryGetAsync("key1");
        var result2 = await index.TryGetAsync("key2");
        result1.Should().Be(messageId1);
        result2.Should().Be(messageId2);
    }

    [TestMethod]
    public async Task ClearAsync_RemovesAllEntries()
    {
        // Arrange
        var index = new DeduplicationIndex();
        await index.TryAddAsync("key1", Guid.NewGuid());
        await index.TryAddAsync("key2", Guid.NewGuid());

        // Act
        await index.ClearAsync();

        // Assert
        var count = await index.GetCountAsync();
        count.Should().Be(0);
    }

    [TestMethod]
    public async Task ConcurrentOperations_HandleMultipleThreads()
    {
        // Arrange
        var index = new DeduplicationIndex();
        var tasks = new Task[50];

        // Act - Add keys concurrently
        for (int i = 0; i < 50; i++)
        {
            int index_i = i;
            tasks[i] = Task.Run(async () => await index.TryAddAsync($"key{index_i}", Guid.NewGuid()));
        }

        await Task.WhenAll(tasks);

        // Assert
        var count = await index.GetCountAsync();
        count.Should().Be(50);
    }
}
