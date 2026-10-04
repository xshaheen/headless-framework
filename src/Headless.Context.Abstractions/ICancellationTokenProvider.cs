// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// Supplies a <see cref="CancellationToken"/> representing the lifetime or cancellation scope of the
/// current ambient context, such as an HTTP request's <c>RequestAborted</c> token or an
/// application-shutdown token.
/// </summary>
/// <remarks>
/// Inject this into services that must cooperate with request cancellation without receiving
/// the token directly as a method parameter.
/// </remarks>
public interface ICancellationTokenProvider
{
    /// <summary>Gets the cancellation token for the current ambient scope.</summary>
    CancellationToken Token { get; }
}

/// <summary>
/// <see cref="ICancellationTokenProvider"/> implementation that always returns <see cref="CancellationToken.None"/>.
/// </summary>
/// <remarks>
/// Use as a no-op default in singleton services, background jobs, or tests where no real cancellation
/// scope exists. Exposed as a singleton via <see cref="Instance"/> to avoid unnecessary allocations.
/// </remarks>
public sealed class DefaultCancellationTokenProvider : ICancellationTokenProvider
{
    /// <summary>Gets the shared singleton instance.</summary>
    public static DefaultCancellationTokenProvider Instance { get; } = new();

    private DefaultCancellationTokenProvider() { }

    /// <inheritdoc/>
    public CancellationToken Token => CancellationToken.None;
}

[PublicAPI]
public static class CancellationTokenProviderExtensions
{
    /// <summary>
    /// Returns <paramref name="preferredValue"/> when it is a real (non-<see cref="CancellationToken.None"/>)
    /// token, otherwise falls back to the provider's token.
    /// </summary>
    /// <remarks>
    /// Override semantics, not linking: when an explicit token is supplied the provider's token is not observed.
    /// If both signals must be honored, link them with
    /// <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, CancellationToken)"/>.
    /// </remarks>
    /// <param name="provider">The provider whose token is used when <paramref name="preferredValue"/> is <see cref="CancellationToken.None"/>.</param>
    /// <param name="preferredValue">
    /// The caller-supplied token to prefer. Defaults to <see cref="CancellationToken.None"/>, which
    /// triggers fallback to <paramref name="provider"/>'s token.
    /// </param>
    /// <returns>
    /// <paramref name="preferredValue"/> if it is not <see cref="CancellationToken.None"/>;
    /// otherwise <see cref="ICancellationTokenProvider.Token"/> from <paramref name="provider"/>.
    /// </returns>
    public static CancellationToken FallbackToProvider(
        this ICancellationTokenProvider provider,
        CancellationToken preferredValue = default
    )
    {
        return preferredValue == CancellationToken.None ? provider.Token : preferredValue;
    }
}
