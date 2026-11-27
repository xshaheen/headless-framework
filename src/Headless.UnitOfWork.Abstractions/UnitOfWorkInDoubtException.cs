// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.CompleteAsync" /> (and so by every <c>RunAsync</c>) when the commit operation
/// failed without establishing whether the transaction committed. <see cref="Exception.InnerException" /> carries
/// the original fault.
/// </summary>
/// <remarks>
/// <para>
/// The unit is <see cref="UnitOfWorkState.Failed" /> with <see cref="UnitOfWorkFailureReason.InDoubt" />, and its
/// <c>OnCompleted</c> work did not run. A <c>RunAsync</c> caller never holds the unit, so this type is how it learns
/// the outcome is unknown rather than a certain rollback.
/// </para>
/// <para>
/// Do not retry the business operation blindly: a retry can apply it twice. Check the operation's durable
/// idempotency key first (<c>IIdempotentOperations.PeekAsync</c>, or re-admit it) and retry only when the first
/// attempt is known not to have committed. Rows enlisted through <c>unit.Outbox</c> and <c>unit.Jobs</c> committed
/// or rolled back with the transaction; when they committed, the relay and the jobs poller deliver them.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkInDoubtException : InvalidOperationException
{
    private const string _DefaultMessage =
        "The unit of work's commit faulted without a confirmed outcome. Check the operation's durable idempotency key before retrying. The inner exception carries the original fault.";

    /// <summary>Initializes the exception with the default message.</summary>
    public UnitOfWorkInDoubtException()
        : base(_DefaultMessage) { }

    /// <summary>Initializes the exception with <paramref name="message" />.</summary>
    /// <param name="message">The message that describes the in-doubt commit.</param>
    public UnitOfWorkInDoubtException(string message)
        : base(message) { }

    /// <summary>Initializes the exception with <paramref name="message" /> and the commit operation's fault.</summary>
    /// <param name="message">The message that describes the in-doubt commit.</param>
    /// <param name="innerException">The original fault raised by the commit operation.</param>
    public UnitOfWorkInDoubtException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Initializes the exception with the default message and the commit operation's fault.</summary>
    /// <param name="innerException">The original fault raised by the commit operation.</param>
    public UnitOfWorkInDoubtException(Exception innerException)
        : base(_DefaultMessage, innerException) { }
}
