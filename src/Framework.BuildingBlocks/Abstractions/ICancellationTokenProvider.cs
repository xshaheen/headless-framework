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

public static class CancellationTokenProviderExtensions
{
    public static CancellationToken FallbackToProvider(
        this ICancellationTokenProvider provider,
        CancellationToken prefferedValue = default
    )
    {
        return prefferedValue == CancellationToken.None ? provider.Token : prefferedValue;
    }
}
