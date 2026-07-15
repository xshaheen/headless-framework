// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Caching-wide instrumentation configuration resolved by every cache provider from DI. Registered once by
/// <c>AddHeadlessCaching</c> from <see cref="HeadlessCachingSetupBuilder.IncludeKeyInTraces"/>; providers thread
/// it into the <see cref="FactoryCacheCoordinator"/> and their own span emission. Must-be-public plumbing (DI
/// resolves it into provider constructors); not intended for direct use.
/// </summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheInstrumentationConfig
{
    /// <summary>
    /// Whether spans may carry the raw cache key on the <c>headless.cache.key</c> attribute. Default
    /// <see langword="false"/>: cache keys routinely carry tenant/user identifiers and PII cannot be un-leaked
    /// from a trace backend. The key is never a metric dimension regardless of this flag.
    /// </summary>
    public bool IncludeKeyInTraces { get; init; }
}
