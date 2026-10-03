// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Caching;

/// <summary>The low-level distributed (L2) tier events of a hybrid cache.</summary>
[PublicAPI]
public interface ICacheDistributedEvents
{
    /// <summary>The L2 store read hit.</summary>
    IAsyncEvent<CacheKeyEventArgs> Hit { get; }

    /// <summary>The L2 store read missed.</summary>
    IAsyncEvent<CacheKeyEventArgs> Miss { get; }
}
