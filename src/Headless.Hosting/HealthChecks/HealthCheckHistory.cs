// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>Represents one registration and its recorded results.</summary>
/// <param name="Descriptor">The registration.</param>
/// <param name="Results">The last 30 recorded results, newest last. Empty before the first probe.</param>
[PublicAPI]
public sealed record HealthCheckHistory(
    HealthCheckDescriptor Descriptor,
    IReadOnlyList<HealthCheckProbeResult> Results
);
