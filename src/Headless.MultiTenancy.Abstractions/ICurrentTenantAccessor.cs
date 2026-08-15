// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.MultiTenancy;

/// <summary>
/// Low-level storage slot for the ambient <see cref="TenantInformation"/> in the current
/// execution context. Higher-level code should prefer <see cref="ICurrentTenant"/>;
/// this interface is intended for framework infrastructure that needs direct read/write access
/// to the raw tenant slot (for example, middleware that sets the tenant from a JWT claim
/// before the request handler runs).
/// </summary>
public interface ICurrentTenantAccessor
{
    /// <summary>
    /// Gets or sets the ambient tenant information for the current execution context.
    /// <para>A <see langword="null"/> value indicates that the tenant has not been set explicitly.</para>
    /// <para>A non-<see langword="null"/> value with a <see langword="null"/> <see cref="TenantInformation.TenantId"/>
    /// indicates that the tenant context has been explicitly cleared (null tenant id set).</para>
    /// <para>A non-<see langword="null"/> value with a non-<see langword="null"/> <see cref="TenantInformation.TenantId"/>
    /// indicates an active, identified tenant context.</para>
    /// </summary>
    TenantInformation? Current { get; set; }
}
