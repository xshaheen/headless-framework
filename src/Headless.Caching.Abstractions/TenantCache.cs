// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Convenience <see cref="ScopedCache{T}"/> that prefixes keys with <c>t:{tenantId}</c>,
/// providing transparent tenant isolation for any cache item type.
/// </summary>
/// <remarks>
/// Safe to register as singleton — <paramref name="tenantIdProvider"/> is invoked on each
/// operation, reading from ambient tenant context (e.g. <c>ICurrentTenant.Id</c>).
/// </remarks>
[PublicAPI]
public sealed class TenantCache<T>(ICache cache, Func<string?> tenantIdProvider)
    : ScopedCache<T>(cache, () => $"t:{tenantIdProvider()}");
