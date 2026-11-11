// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Demo;

public sealed class Counter
{
    public int Id { get; set; }

    public int Value { get; set; }

    /// <summary>The concurrency token: a save that read an older version fails instead of overwriting a newer one.</summary>
    public int Version { get; set; }
}
