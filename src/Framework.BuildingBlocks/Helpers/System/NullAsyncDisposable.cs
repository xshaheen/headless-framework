// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.BuildingBlocks.Helpers.System;

[PublicAPI]
public sealed class NullAsyncDisposable : IAsyncDisposable
{
    private NullAsyncDisposable() { }

    public ValueTask DisposeAsync() => default;

    public static NullAsyncDisposable Instance { get; } = new();
}
