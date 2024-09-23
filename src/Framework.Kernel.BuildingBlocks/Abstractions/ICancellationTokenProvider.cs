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
