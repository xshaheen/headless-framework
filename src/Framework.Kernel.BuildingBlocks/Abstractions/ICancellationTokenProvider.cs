// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Kernel.BuildingBlocks.Abstractions;

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
        return prefferedValue == default || prefferedValue == CancellationToken.None ? provider.Token : prefferedValue;
    }
}
