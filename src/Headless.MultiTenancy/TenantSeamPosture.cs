// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Configured tenant posture for a single Headless seam.</summary>
/// <param name="Seam">The seam name.</param>
/// <param name="Status">The seam posture status.</param>
/// <param name="Capabilities">Non-PII capability labels reported by the seam.</param>
/// <param name="RuntimeMarkers">Non-PII runtime markers reported by the seam.</param>
[PublicAPI]
public sealed record TenantSeamPosture(
    string Seam,
    TenantPostureStatus Status,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> RuntimeMarkers
);
