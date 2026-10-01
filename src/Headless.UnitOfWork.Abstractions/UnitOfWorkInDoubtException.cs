// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.CompleteAsync" /> (and so by every <c>RunAsync</c>) when the commit request may
/// have reached the database but the connection failed or timed out before an answer came back: the transaction
/// may or may not have committed. The driver's fault is the <see cref="Exception.InnerException" />.
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
        "The unit of work's commit may have reached the database, but the connection failed before the outcome was known, so the transaction may or may not have committed. Check the operation's durable idempotency key before retrying it; the inner exception is the driver's fault.";

    /// <summary>Initializes the exception with the default message.</summary>
    public UnitOfWorkInDoubtException()
        : base(_DefaultMessage) { }

    /// <summary>Initializes the exception with <paramref name="message" />.</summary>
    /// <param name="message">The message that describes the in-doubt commit.</param>
    public UnitOfWorkInDoubtException(string message)
        : base(message) { }

    /// <summary>Initializes the exception with <paramref name="message" /> and the driver's fault.</summary>
    /// <param name="message">The message that describes the in-doubt commit.</param>
    /// <param name="innerException">The driver's fault raised by the commit.</param>
    public UnitOfWorkInDoubtException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Initializes the exception with the default message and the driver's fault.</summary>
    /// <param name="innerException">The driver's fault raised by the commit.</param>
    public UnitOfWorkInDoubtException(Exception innerException)
        : base(_DefaultMessage, innerException) { }
}
