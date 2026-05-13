// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Dispatches local messages and transactionally enqueues distributed messages collected during EF saves.
/// </summary>
/// <remarks>
/// Distributed methods are invoked before the EF transaction commits and may be retried by the EF execution
/// strategy. Implementations MUST NOT perform non-transactional external broker publishes from these methods.
/// Enlist the messages in <c>currentTransaction</c> (for example, a transactional outbox), or make
/// enqueueing idempotent by each message <c>UniqueId</c> and publish to external infrastructure only after the
/// transaction commits.
/// </remarks>
public interface IHeadlessMessageDispatcher
{
    /// <summary>Publishes local messages within the current save transaction.</summary>
    Task PublishLocalAsync(
        IReadOnlyList<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    /// <summary>Publishes local messages within the current save transaction.</summary>
    void PublishLocal(IReadOnlyList<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction);

    /// <summary>
    /// Enqueues distributed messages into transaction-bound storage for post-commit delivery.
    /// </summary>
    Task EnqueueDistributedAsync(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Enqueues distributed messages into transaction-bound storage for post-commit delivery.
    /// </summary>
    void EnqueueDistributed(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    );
}

internal sealed class ThrowHeadlessMessageDispatcher : IHeadlessMessageDispatcher
{
    public Task PublishLocalAsync(
        IReadOnlyList<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => throw _CreateMissingDispatcherException(emitters.Count, "local");

    public void PublishLocal(IReadOnlyList<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction) =>
        throw _CreateMissingDispatcherException(emitters.Count, "local");

    public Task EnqueueDistributedAsync(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => throw _CreateMissingDispatcherException(emitters.Count, "distributed");

    public void EnqueueDistributed(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    ) => throw _CreateMissingDispatcherException(emitters.Count, "distributed");

    private static InvalidOperationException _CreateMissingDispatcherException(int emitterCount, string messageKind)
    {
        FormattableString template =
            $"Headless EF collected {emitterCount} {messageKind} message emitter(s), but no application {nameof(IHeadlessMessageDispatcher)} is registered. Register an {nameof(IHeadlessMessageDispatcher)} implementation or disable message-emitting save processors.";

        return new(template.ToString(CultureInfo.InvariantCulture));
    }
}
