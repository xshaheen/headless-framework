// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>Configures one <see cref="IRequestClient"/> call.</summary>
/// <remarks>
/// This record does not derive from <see cref="MessageOptions"/>, because a request refuses most of what a send
/// accepts: the framework owns the message identifier, and a request is always sent directly, never delayed,
/// scheduled, given a callback name, or captured durably.
/// </remarks>
[PublicAPI]
public sealed record RequestOptions
{
    /// <summary>
    /// Gets the longest <see cref="Timeout"/> a call may ask for, and the longest default a host may configure: 10
    /// minutes. A finished call keeps a small tombstone and a timer for its whole timeout, so the bound keeps the
    /// caller's tracked entries proportional to its request rate.
    /// </summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets how long the caller waits for the reply. <see langword="null"/> uses the host's default request timeout.
    /// </summary>
    /// <remarks>
    /// The timeout also sets the request's deadline, after which the responder neither starts the work nor replies.
    /// A timeout is ambiguous: the responder may still have completed the work.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is zero or negative, or longer than <see cref="MaxTimeout"/>.
    /// </exception>
    public TimeSpan? Timeout
    {
        get;
        init =>
            field = value is { } timeout
                ? Argument.IsLessThanOrEqualTo(
                    Argument.IsPositive(timeout, paramName: nameof(Timeout)),
                    MaxTimeout,
                    paramName: nameof(Timeout)
                )
                : null;
    }

    /// <summary>
    /// Gets the explicit tenant for the request. <see langword="null"/> uses the ambient tenant when the host propagates
    /// tenants. The reply must carry the same tenant, or the caller drops it.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the explicit correlation identifier override for the request message.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets custom application headers for the request message. Reserved messaging headers are rejected, and header
    /// names and values cannot contain control characters.
    /// </summary>
    public IDictionary<string, string?>? Headers { get; init; }
}
