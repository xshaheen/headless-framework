// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Messaging;

/// <summary>
/// One unit-of-work handle bound to the messaging outbox feature, returned by <c>unit.Outbox</c>. Publishing
/// through it writes the durable row inside that unit's transaction, so a rollback discards the message.
/// </summary>
/// <remarks>
/// A small binding created on each read of <c>unit.Outbox</c>; it owns nothing to dispose. Read it at the call
/// site rather than storing it: the handle's liveness is checked when a publish runs, not when the binding is
/// taken, so a retained binding whose nested view has since completed throws on its next publish.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkOutbox
{
    private readonly IUnitOfWorkOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkOutbox(IUnitOfWorkOutbox outbox, IUnitOfWork unitOfWork)
    {
        _outbox = outbox;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Publishes a broadcast (bus lane) message inside the bound unit's transaction.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null" />.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A receipt with the resolved message identity and the durable row handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bound handle can no longer carry work, or the storage cannot join the unit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public Task<PublishReceipt> PublishAsync<T>(T? contentObj, CancellationToken cancellationToken = default)
    {
        return PublishAsync(contentObj, options: null, cancellationToken);
    }

    /// <summary>Publishes a broadcast (bus lane) message inside the bound unit's transaction.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null" />.</param>
    /// <param name="options">Optional overrides for message name, correlation, headers, and delivery delay.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A receipt with the resolved message identity and the durable row handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bound handle can no longer carry work, or the storage cannot join the unit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public Task<PublishReceipt> PublishAsync<T>(
        T? contentObj,
        OutboxPublishOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        return _outbox.PublishAsync(_unitOfWork, contentObj, options, cancellationToken);
    }

    /// <summary>Enqueues a point-to-point (queue lane) message inside the bound unit's transaction.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null" />.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A receipt with the resolved message identity and the durable row handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bound handle can no longer carry work, or the storage cannot join the unit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public Task<PublishReceipt> EnqueueAsync<T>(T? contentObj, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(contentObj, options: null, cancellationToken);
    }

    /// <summary>Enqueues a point-to-point (queue lane) message inside the bound unit's transaction.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null" />.</param>
    /// <param name="options">Optional overrides for message name, correlation, headers, and delivery delay.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A receipt with the resolved message identity and the durable row handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bound handle can no longer carry work, or the storage cannot join the unit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public Task<PublishReceipt> EnqueueAsync<T>(
        T? contentObj,
        OutboxQueueOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        return _outbox.EnqueueAsync(_unitOfWork, contentObj, options, cancellationToken);
    }
}
