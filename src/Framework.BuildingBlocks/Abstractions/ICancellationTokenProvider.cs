// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.BuildingBlocks.Abstractions;

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
    public static CancellationToken FallbackToProvider(
        this ICancellationTokenProvider provider,
        CancellationToken preferredValue = default
    )
    {
        return preferredValue == CancellationToken.None ? provider.Token : preferredValue;
    }
}
