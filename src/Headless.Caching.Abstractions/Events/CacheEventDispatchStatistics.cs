// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>A point-in-time snapshot of a cache instance's best-effort event dispatcher.</summary>
/// <param name="Accepted">Signals accepted into the bounded FIFO.</param>
/// <param name="Processed">Accepted signals whose handler snapshot finished running.</param>
/// <param name="Dropped">Signals rejected because the FIFO was full or shutting down.</param>
/// <param name="Pending">Accepted signals not yet finished, including the signal currently being handled.</param>
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
