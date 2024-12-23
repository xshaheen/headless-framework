// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Core;

[PublicAPI]
public sealed class NullDisposable : IDisposable
{
    private NullDisposable() { }

    public void Dispose() { }

    public static NullDisposable Instance { get; } = new();
}
