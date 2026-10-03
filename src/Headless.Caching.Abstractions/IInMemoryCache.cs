// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// The in-memory (local / L1) tier contract. Carries no members beyond <see cref="ICache"/>: it is a deliberate
/// tier marker, not a behavioral extension. Multi-tier composition (for example the hybrid cache resolving its
/// L1 tier) selects the in-memory tier by this type, distinctly from <see cref="IRemoteCache"/> — both are
/// <see cref="ICache"/>, so the marker is what disambiguates them in DI. Do not remove it for being empty: a
/// hybrid host depends on this type to resolve the local tier.
/// </summary>
[PublicAPI]
public interface IInMemoryCache : ICache;
