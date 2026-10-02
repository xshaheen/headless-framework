// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines a type-safe responder: a Queue consumer that answers each <typeparamref name="TRequest"/> with one
/// <typeparamref name="TResponse"/> that the requesting process awaits.
/// </summary>
/// <typeparam name="TRequest">The request message type. Must be a reference type.</typeparam>
/// <typeparam name="TResponse">The response type returned to the requester. Must be a reference type.</typeparam>
/// <remarks>
/// <para>
/// <strong>Request/reply versus callbacks:</strong> a responder answers a caller that is waiting on
/// <c>IRequestClient.RequestAsync</c>, and the reply goes only to the calling process. The
/// <see cref="ConsumeContext.SetResponse{TResponse}(TResponse)"/> callback of an <see cref="IConsume{TMessage}"/> consumer instead
/// publishes a new Bus message that nobody awaits. A responder cannot publish a callback response.
/// </para>
/// <para>
/// <strong>Registration:</strong> mark the class with <see cref="QueueConsumerAttribute"/>. A responder is the message's
/// one Queue consumer, so the same request type cannot also have a plain <see cref="IConsume{TMessage}"/> Queue
/// consumer. A responder reached by a plain enqueue rather than a request runs and its result is discarded.
/// </para>
/// <para>
/// <strong>Outcome:</strong> the reply leaves only after the handler's outcome is durable. A thrown exception that is
/// not retried, and a <see langword="null"/> result, reach the caller as a fault carrying a code from
/// <c>RequestFaultCodes</c>.
/// </para>
/// <para>
/// <strong>Example:</strong>
/// <code>
/// [QueueConsumer("pricing.get-quote")]
/// public sealed class GetQuoteResponder(IPricing pricing) : IRespond&lt;GetQuote, Quote&gt;
/// {
///     public async ValueTask&lt;Quote&gt; RespondAsync(
///         ConsumeContext&lt;GetQuote&gt; context,
///         CancellationToken cancellationToken
///     )
///     {
///         return await pricing.QuoteAsync(context.Message.Sku, cancellationToken);
///     }
/// }
/// </code>
/// </para>
/// </remarks>
[PublicAPI]
public interface IRespond<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    /// <summary>Handles a request and produces the response returned to the requester.</summary>
    /// <param name="context">The consumption context containing the request payload and metadata.</param>
    /// <param name="cancellationToken">
    /// A token canceled when the host stops. It is not tied to the request's deadline or to the caller's cancellation,
    /// because work the responder accepted continues after the caller stops waiting.
    /// </param>
    /// <returns>The response to send to the requester. Must not be <see langword="null"/>.</returns>
    ValueTask<TResponse> RespondAsync(ConsumeContext<TRequest> context, CancellationToken cancellationToken);
}
