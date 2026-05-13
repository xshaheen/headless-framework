// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Pairs a <see cref="ILocalMessageEmitter"/> with the snapshot of local messages collected from it
/// during the current <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// The constructor parameter is captured by value and then deduplicated by <see cref="ILocalMessage.UniqueId"/>.
/// Subsequent mutations to the source list on the emitter never leak into the dispatched snapshot.
/// </remarks>
public sealed record EmitterLocalMessages(ILocalMessageEmitter Emitter, IReadOnlyList<ILocalMessage> Messages)
{
    /// <summary>
    /// Returns a deduplicated snapshot of the constructor argument keyed by
    /// <see cref="ILocalMessage.UniqueId"/>. Deconstruct returns this snapshot, not the input.
    /// </summary>
    public IReadOnlyList<ILocalMessage> Messages { get; } =
        EmitterMessagesSnapshot.Snapshot(Messages, static m => m.UniqueId);
}

/// <summary>
/// Pairs a <see cref="IDistributedMessageEmitter"/> with the snapshot of distributed messages collected
/// from it during the current <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// The constructor parameter is captured by value and then deduplicated by <see cref="IDistributedMessage.UniqueId"/>.
/// Subsequent mutations to the source list on the emitter never leak into the dispatched snapshot.
/// </remarks>
public sealed record EmitterDistributedMessages(
    IDistributedMessageEmitter Emitter,
    IReadOnlyList<IDistributedMessage> Messages
)
{
    /// <summary>
    /// Returns a deduplicated snapshot of the constructor argument keyed by
    /// <see cref="IDistributedMessage.UniqueId"/>. Deconstruct returns this snapshot, not the input.
    /// </summary>
    public IReadOnlyList<IDistributedMessage> Messages { get; } =
        EmitterMessagesSnapshot.Snapshot(Messages, static m => m.UniqueId);
}

internal static class EmitterMessagesSnapshot
{
    // Snapshots the caller's list so subsequent mutation on the emitter doesn't leak into the pipeline,
    // and deduplicates by the supplied UniqueId accessor.
    public static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> messages, Func<T, string> uniqueId)
    {
        return messages.Count switch
        {
            0 => [],
            1 => [messages[0]],
            _ => messages.DistinctBy(uniqueId, StringComparer.Ordinal).ToArray(),
        };
    }
}
