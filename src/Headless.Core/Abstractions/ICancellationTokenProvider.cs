// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

public interface ICancellationTokenProvider
{
    CancellationToken Token { get; }
}

public sealed class DefaultCancellationTokenProvider : ICancellationTokenProvider
{
    public static DefaultCancellationTokenProvider Instance { get; } = new();

    private DefaultCancellationTokenProvider() { }

    public CancellationToken Token => CancellationToken.None;
}

[PublicAPI]
public static class CancellationTokenProviderExtensions
{
    /// <summary>
    /// Returns <paramref name="preferredValue"/> when it is a real (non-<see cref="CancellationToken.None"/>)
    /// token, otherwise falls back to the provider's token. Override semantics, not linking: when an explicit
    /// token is supplied the provider's token is NOT observed. If both signals must be honored, link them with
    /// <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, CancellationToken)"/>.
    /// </summary>
    public static CancellationToken FallbackToProvider(
        this ICancellationTokenProvider provider,
        CancellationToken preferredValue = default
    )
    {
        return preferredValue == CancellationToken.None ? provider.Token : preferredValue;
    }
}
