// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Caching;

/// <summary>Represents a point-in-time snapshot of dispatch statistics for a cache instance.</summary>
/// <param name="Accepted">Signals accepted into the bounded queue.</param>
/// <param name="Processed">Accepted signals whose handler snapshot finished running.</param>
/// <param name="Dropped">Signals rejected because the queue was full or shutting down.</param>
/// <param name="Pending">Accepted signals not yet completed, including the signal currently executing.</param>
/// <param name="Capacity">The maximum number of signals buffered behind the active handler.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct CacheEventDispatchStatistics(
    long Accepted,
    long Processed,
    long Dropped,
    long Pending,
    int Capacity
);
