// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Sends a request on the Queue lane and awaits the one typed response its responder returns.
/// </summary>
/// <remarks>
/// <para>
/// A request is an ordinary Queue message, so exactly one <see cref="IRespond{TRequest, TResponse}"/> consumer receives
/// it, and it passes through the Queue publish and consume middleware. It is always sent directly: it is never captured
/// in the outbox, delayed, or scheduled. The reply returns only to the calling process.
/// </para>
/// <para>
/// The caller's continuation is at-most-once. When the call fails with <see cref="RequestTimeoutException"/> or
/// <see cref="RequestAbortedException"/>, the responder may still have completed the work. Canceling the call ends only
/// the wait; work the responder already accepted continues.
/// </para>
/// <para>
/// Use <see cref="ConsumeContext.SetResponse{TResponse}(TResponse)"/> callbacks instead when nothing waits for the
/// answer. A call made from inside a transactional inbox unit throws, because waiting there would hold the database
/// transaction for the whole timeout.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRequestClient
{
    /// <summary>Sends <paramref name="request"/> and waits for its response.</summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response type the responder returns.</typeparam>
    /// <param name="request">The request payload.</param>
    /// <param name="options">Optional per-call timeout, tenant, correlation, and custom headers.</param>
    /// <param name="cancellationToken">A token that ends the wait. It does not recall a request already sent.</param>
    /// <returns>The responder's response. Never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="RequestTimeoutException">No reply arrived before the timeout.</exception>
    /// <exception cref="RequestFaultedException">The responder answered with a fault reply.</exception>
    /// <exception cref="ResponseContractMismatchException">
    /// The reply carries a response contract other than the one expected for <typeparamref name="TResponse"/>.
    /// </exception>
    /// <exception cref="RequestNotSentException">The request never left the caller.</exception>
    /// <exception cref="RequestAbortedException">The requesting host stopped before the reply arrived.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call runs inside a transactional inbox unit, or <see cref="RequestOptions.Headers"/> contains a reserved
    /// messaging header or control characters.
    /// </exception>
    Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where TRequest : class
        where TResponse : class;
}
