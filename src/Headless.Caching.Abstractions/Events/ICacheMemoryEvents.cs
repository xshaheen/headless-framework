// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Caching;

/// <summary>The low-level memory (L1) tier events of a hybrid cache.</summary>
[PublicAPI]
public interface ICacheMemoryEvents
{
    /// <summary>The L1 store read hit.</summary>
    IAsyncEvent<CacheKeyEventArgs> Hit { get; }

    /// <summary>The L1 store read missed.</summary>
    IAsyncEvent<CacheKeyEventArgs> Miss { get; }
}
