// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// Supplies a <see cref="CancellationToken"/> representing the cancellation scope of the current ambient execution context.
/// </summary>
public interface ICancellationTokenProvider
{
    /// <summary>Gets the cancellation token for the current ambient scope.</summary>
    CancellationToken Token { get; }
}

/// <summary>
/// Provides an <see cref="ICancellationTokenProvider"/> implementation that returns <see cref="CancellationToken.None"/>.
/// </summary>
/// <remarks>
/// Used as a default implementation when no ambient cancellation scope is available.
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
    /// Returns <paramref name="preferredValue"/> when it is not <see cref="CancellationToken.None"/>;
    /// otherwise falls back to the provider token.
    /// </summary>
    /// <remarks>
    /// This method overrides rather than links tokens: when an explicit non-default token is supplied, the provider token is ignored.
    /// </remarks>
    /// <param name="provider">The provider whose token is used when <paramref name="preferredValue"/> is <see cref="CancellationToken.None"/>.</param>
    /// <param name="preferredValue">
    /// The caller-supplied token to prefer. Defaults to <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// <paramref name="preferredValue"/> when it is not <see cref="CancellationToken.None"/>;
    /// otherwise <see cref="ICancellationTokenProvider.Token"/> from <paramref name="provider"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    public static CancellationToken FallbackToProvider(
        this ICancellationTokenProvider provider,
        CancellationToken preferredValue = default
    )
    {
        return preferredValue == CancellationToken.None ? provider.Token : preferredValue;
    }
}
