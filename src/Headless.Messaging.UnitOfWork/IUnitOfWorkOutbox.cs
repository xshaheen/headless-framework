// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Messaging;

/// <summary>
/// The enlisted publish capability attached to a unit of work by the messaging registration, and the contract
/// behind <c>unit.Outbox</c>. Reached through <see cref="IUnitOfWork.GetFeature{TFeature}" />; implemented in
/// <c>Headless.Messaging.Core</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every publish here writes its durable row inside the given unit's transaction: the row becomes visible when
/// the unit completes, and a rollback discards it. This is the whole difference from <see cref="IBus" /> and
/// <see cref="IQueue" />, which are autonomous and whose rows survive the caller's rollback.
/// </para>
/// <para>
/// It refuses rather than degrades: when the storage cannot join the given unit, the call throws before any
/// storage or transport effect instead of writing a standalone row. Which units a storage can join is the
/// storage's own answer — the in-memory storage joins a resource-less unit through its buffer, the relational
/// storages join only a same-database relational resource.
/// </para>
/// <para>
/// The capability is cached on the unit and holds no view of it, so the caller's own handle travels as an
/// argument on every call: a child view can complete while the unit stays active, and the implementation checks
/// that handle's liveness per publish rather than once. Callers reach this through the <c>unit.Outbox</c>
/// accessor, which binds the capability to the handle it was read from.
/// </para>
/// <para>
/// A publish written through this accessor forfeits execution-strategy replay for the rest of the unit: the
/// durable row is written outside the change tracker, so a retrying strategy cannot re-run the unit without
/// duplicating it. Integration events the save pipeline emits are exempt, because it re-publishes them on a
/// replayed attempt; a unit that mixes both is no longer retriable, since the direct publish marks it.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkOutbox
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
    /// The handle can no longer carry work (it completed, or the unit reached a terminal state), or the storage
    /// cannot join the unit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The handle was disposed.</exception>
    Task<PublishReceipt> PublishAsync<T>(
        IUnitOfWork unitOfWork,
        T? contentObj,
        OutboxPublishOptions? options,
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
    /// The handle can no longer carry work (it completed, or the unit reached a terminal state), or the storage
    /// cannot join the unit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The handle was disposed.</exception>
    Task<PublishReceipt> EnqueueAsync<T>(
        IUnitOfWork unitOfWork,
        T? contentObj,
        OutboxQueueOptions? options,
        CancellationToken cancellationToken = default
    );
}
