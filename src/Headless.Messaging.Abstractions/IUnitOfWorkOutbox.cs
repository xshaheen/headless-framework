// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Messaging;

/// <summary>
/// Plumbing behind <c>unit.Outbox</c>: the enlisted publish feature <c>AddHeadlessMessaging</c> registers, which a
/// unit of work resolves through <see cref="IUnitOfWork.GetFeature{TFeature}" />. Application code publishes
/// through the <see cref="UnitOfWorkOutbox" /> binding that <c>unit.Outbox</c> returns, which supplies the unit;
/// this interface is public only so the unit-of-work packages can hand it out without referencing messaging.
/// </summary>
/// <remarks>
/// <para>
/// Every publish here writes its durable row inside the given unit's transaction: the row becomes visible when
/// the unit completes, and a rollback discards it. This is the whole difference from <c>IBus</c> and
/// <c>IQueue</c>, which are autonomous and whose rows survive the caller's rollback.
/// </para>
/// <para>
/// It refuses rather than degrades: when the storage cannot join the given unit, the call throws before any
/// storage or transport effect instead of writing a standalone row. Which units a storage can join is the
/// storage's own answer — the in-memory storage joins a resource-less unit through its buffer, the relational
/// storages join only a same-database relational resource.
/// </para>
/// <para>
/// A singleton that holds no unit: the caller's own handle travels as an argument on every call, and the outbox
/// writer's first registration on that handle is what refuses one that can no longer carry work — a completed
/// nested view, a terminal unit — before any storage effect.
/// </para>
/// <para>
/// A publish written through this accessor ends execution-strategy replay only where a replay would not re-run
/// it. Issued directly inside the caller's own <c>RunAsync(db, …)</c> block (an owned unit), the block's replay
/// re-runs the publish, so the unit stays replayable. Issued into the <c>HeadlessDbContext</c> save pipeline's
/// own save (an observed unit), which replays without re-running the domain-event handlers, the publish calls
/// <see cref="IUnitOfWork.PreventRetry" /> before it writes; the integration events that pipeline emits are
/// exempt because it re-publishes them on a replayed attempt. A handler publishing during the caller's own
/// <c>SaveChangesAsync</c> ends replay as well: that save marks the unit once it clears the events a replayed
/// block would need to re-dispatch.
/// </para>
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkOutbox : IUnitOfWorkFeature
{
    /// <summary>
    /// Publishes a broadcast (bus lane) message inside <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="unitOfWork">The caller's own handle — the view the publish is checked against and enlisted in.</param>
    /// <param name="contentObj">The message payload. Can be <see langword="null" />.</param>
    /// <param name="options">Optional overrides for message name, correlation, headers, and delivery delay.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A receipt with the resolved message identity and the durable row handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    /// The handle can no longer carry work (a nested view that completed, or a unit in a terminal state), or the
    /// storage cannot join the unit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The handle was disposed.</exception>
    Task<PublishReceipt> PublishAsync<T>(
        IUnitOfWork unitOfWork,
        T? contentObj,
        OutboxOptions? options,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Enqueues a point-to-point (queue lane) message inside <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="unitOfWork">The caller's own handle — the view the enqueue is checked against and enlisted in.</param>
    /// <param name="contentObj">The message payload. Can be <see langword="null" />.</param>
    /// <param name="options">Optional overrides for message name, correlation, headers, and delivery delay.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A receipt with the resolved message identity and the durable row handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    /// The handle can no longer carry work (a nested view that completed, or a unit in a terminal state), or the
    /// storage cannot join the unit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The handle was disposed.</exception>
    Task<PublishReceipt> EnqueueAsync<T>(
        IUnitOfWork unitOfWork,
        T? contentObj,
        OutboxOptions? options,
        CancellationToken cancellationToken = default
    );
}
