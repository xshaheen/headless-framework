// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>Marks that <c>AddTenantScopedCache&lt;T&gt;()</c> already registered <see cref="ICache{T}"/>.</summary>
internal sealed class TenantScopedCacheRegistration<T>;
