// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Messaging;

/// <summary>
/// One unit-of-work handle bound to the messaging outbox capability, returned by <c>unit.Outbox</c>. Publishing
/// through it writes the durable row inside that unit's transaction, so a rollback discards the message.
/// </summary>
/// <remarks>
/// The binding is a value, not a resource: it holds the capability cached on the unit plus the handle it was
/// read from, and owns nothing to dispose. Taking it is free, so read <c>unit.Outbox</c> at the call site rather
/// than storing it — a retained binding whose handle has since completed throws on its next publish, because
/// liveness is checked per publish, not when the binding is taken.
/// </remarks>
[PublicAPI]
public readonly record struct UnitOfWorkOutbox
{
    private readonly IUnitOfWorkOutbox? _outbox;
    private readonly IUnitOfWork? _unitOfWork;

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
    /// The bound handle can no longer carry work, the storage cannot join the unit, or this binding is the
    /// default value rather than one taken from <c>unit.Outbox</c>.
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
    /// The bound handle can no longer carry work, the storage cannot join the unit, or this binding is the
    /// default value rather than one taken from <c>unit.Outbox</c>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public Task<PublishReceipt> PublishAsync<T>(
        T? contentObj,
        OutboxPublishOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        var (outbox, unitOfWork) = _Unwrap();

        return outbox.PublishAsync(unitOfWork, contentObj, options, cancellationToken);
    }

    /// <summary>Enqueues a point-to-point (queue lane) message inside the bound unit's transaction.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null" />.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A receipt with the resolved message identity and the durable row handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bound handle can no longer carry work, the storage cannot join the unit, or this binding is the
    /// default value rather than one taken from <c>unit.Outbox</c>.
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
    /// The bound handle can no longer carry work, the storage cannot join the unit, or this binding is the
    /// default value rather than one taken from <c>unit.Outbox</c>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public Task<PublishReceipt> EnqueueAsync<T>(
        T? contentObj,
        OutboxQueueOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        var (outbox, unitOfWork) = _Unwrap();

        return outbox.EnqueueAsync(unitOfWork, contentObj, options, cancellationToken);
    }

    // A struct cannot forbid its own default value, and a default binding would otherwise fail with a null
    // dereference far from the mistake that produced it.
    private (IUnitOfWorkOutbox Outbox, IUnitOfWork UnitOfWork) _Unwrap()
    {
        if (_outbox is null || _unitOfWork is null)
        {
            throw new InvalidOperationException(
                "This outbox binding is the default value. Read it from the unit of work you are publishing in, as 'unit.Outbox'."
            );
        }

        return (_outbox, _unitOfWork);
    }
}
