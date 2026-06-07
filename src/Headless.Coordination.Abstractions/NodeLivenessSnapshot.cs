// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Operational liveness snapshot for a current-generation node identity.</summary>
[PublicAPI]
public sealed record NodeLivenessSnapshot(
    NodeIdentity Identity,
    NodeLivenessState State,
    string? Role,
    IReadOnlyDictionary<string, string> Metadata
);
