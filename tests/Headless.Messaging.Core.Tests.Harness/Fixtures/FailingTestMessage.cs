// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Fixtures;

/// <summary>Dedicated failing-path message so the harness can bind a separate topic without remapping TestMessage.</summary>
[PublicAPI]
public sealed record FailingTestMessage
{
    public required string Id { get; init; }

    public required string Name { get; init; }
}
