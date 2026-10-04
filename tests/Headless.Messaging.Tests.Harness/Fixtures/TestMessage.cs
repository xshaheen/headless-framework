// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Fixtures;

/// <summary>Simple test message for transport tests.</summary>
[PublicAPI]
public sealed record TestMessage
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Payload { get; init; }
}
