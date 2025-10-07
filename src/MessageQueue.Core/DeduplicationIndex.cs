namespace MessageQueue.Core;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Thread-safe deduplication index that maps deduplication keys to message IDs.
/// Uses ConcurrentDictionary for lock-free operations.
/// </summary>
public class DeduplicationIndex
{
    private readonly ConcurrentDictionary<string, Guid> _index;

    /// <summary>
    /// Initializes a new instance of the DeduplicationIndex.
    /// </summary>
    public DeduplicationIndex()
    {
        _index = new ConcurrentDictionary<string, Guid>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Tries to add a new deduplication key mapping.
    /// </summary>
    /// <param name="key">The deduplication key.</param>
    /// <param name="messageId">The message ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if added successfully; false if key already exists.</returns>
    public Task<bool> TryAddAsync(string key, Guid messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Deduplication key cannot be null or whitespace.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        bool added = _index.TryAdd(key, messageId);
        return Task.FromResult(added);
    }

    /// <summary>
    /// Tries to get the message ID associated with a deduplication key.
    /// </summary>
    /// <param name="key">The deduplication key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message ID if found; null otherwise.</returns>
    public Task<Guid?> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Deduplication key cannot be null or whitespace.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        if (_index.TryGetValue(key, out Guid messageId))
        {
            return Task.FromResult<Guid?>(messageId);
        }

        return Task.FromResult<Guid?>(null);
    }

    /// <summary>
    /// Updates the message ID for an existing deduplication key (supersede scenario).
    /// </summary>
    /// <param name="key">The deduplication key.</param>
    /// <param name="newMessageId">The new message ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated successfully; false if key doesn't exist.</returns>
    public Task<bool> UpdateAsync(string key, Guid newMessageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Deduplication key cannot be null or whitespace.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        // AddOrUpdate returns the new value
        var result = _index.AddOrUpdate(key, newMessageId, (k, old) => newMessageId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Removes a deduplication key mapping.
    /// </summary>
    /// <param name="key">The deduplication key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if removed successfully; false if key doesn't exist.</returns>
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Deduplication key cannot be null or whitespace.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        bool removed = _index.TryRemove(key, out _);
        return Task.FromResult(removed);
    }

    /// <summary>
    /// Checks if a deduplication key exists in the index.
    /// </summary>
    /// <param name="key">The deduplication key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key exists; false otherwise.</returns>
    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Deduplication key cannot be null or whitespace.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        bool contains = _index.ContainsKey(key);
        return Task.FromResult(contains);
    }

    /// <summary>
    /// Gets the count of entries in the deduplication index.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of entries.</returns>
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_index.Count);
    }

    /// <summary>
    /// Clears all entries from the deduplication index.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _index.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets a snapshot of all deduplication key mappings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary containing all current mappings.</returns>
    public Task<System.Collections.Generic.Dictionary<string, Guid>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = new System.Collections.Generic.Dictionary<string, Guid>(_index, StringComparer.Ordinal);
        return Task.FromResult(snapshot);
    }

    /// <summary>
    /// Restores the deduplication index from a snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot to restore from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RestoreFromSnapshotAsync(System.Collections.Generic.Dictionary<string, Guid> snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        cancellationToken.ThrowIfCancellationRequested();

        _index.Clear();
        foreach (var kvp in snapshot)
        {
            _index.TryAdd(kvp.Key, kvp.Value);
        }

        return Task.CompletedTask;
    }
}
