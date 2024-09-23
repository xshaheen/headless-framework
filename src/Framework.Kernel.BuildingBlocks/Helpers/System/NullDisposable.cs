// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Kernel.BuildingBlocks.Helpers.System;

[PublicAPI]
public sealed class NullDisposable : IDisposable
{
    private NullDisposable() { }

    public void Dispose() { }

    public static NullDisposable Instance { get; } = new();
}
