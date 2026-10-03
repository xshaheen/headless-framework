// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

[PublicAPI]
public static class CancellationTokenProviderExtensions
{
    /// <summary>
    /// Returns <paramref name="preferredValue"/> when it is a real (non-<see cref="CancellationToken.None"/>)
    /// token, otherwise falls back to the provider's token. Override semantics, not linking: when an explicit
    /// token is supplied the provider's token is NOT observed. If both signals must be honored, link them with
    /// <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, CancellationToken)"/>.
    /// </summary>
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
