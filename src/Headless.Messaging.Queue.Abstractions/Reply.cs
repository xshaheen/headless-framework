// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// The reply to an <see cref="IRequestClient.GetReplyAsync{TRequest, TResponse}"/> call: the response with the headers
/// it arrived with.
/// </summary>
/// <typeparam name="TResponse">The response type the responder returns.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Headers"/> holds every header the reply arrived with except the reply protocol's own (every
/// <c>headless-</c> name) and trace propagation headers, so nothing in it can be mistaken for framework metadata. That is
/// usually exactly what the responder set through <see cref="ConsumeContext.SetResponseHeader"/>, but a transport or
/// broker plugin on the reply's path can add a header of its own, and it arrives here too.
/// </para>
/// <para>
/// The values are wire data: treat them as input from another service, not as facts the framework checked.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record Reply<TResponse>
    where TResponse : class
{
    /// <summary>Gets the response. Never <see langword="null"/>.</summary>
    public required TResponse Message { get; init; }

    /// <summary>
    /// Gets the reply's headers other than framework and trace propagation headers, keyed by ordinal name. Empty when
    /// it carried none.
    /// </summary>
    public required IReadOnlyDictionary<string, string?> Headers { get; init; }

    /// <summary>Gets the reply's message identifier, or <see langword="null"/> when the reply carried none.</summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Gets the correlation identifier the reply carried: the request's correlation identifier, or the request's message
    /// identifier when the request had none. <see langword="null"/> when the reply carried none.
    /// </summary>
    public string? CorrelationId { get; init; }
}
