// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>A binder with columns of its own, kept in a second table.</summary>
internal sealed class RingBinder : Binder
{
    public int Rings { get; set; }
}
