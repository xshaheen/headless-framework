// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Abstractions;

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

/// <summary>
/// <see cref="ICurrentTenantAccessor"/> implementation backed by <see cref="AsyncLocal{T}"/>,
/// providing async-flow-isolated tenant context that does not leak across unrelated async
/// branches. This is the default singleton instance used by the framework.
/// </summary>
public sealed class AsyncLocalCurrentTenantAccessor : ICurrentTenantAccessor
{
    /// <summary>
    /// Gets the shared singleton instance. Use this when registering the accessor in the DI container
    /// so that all components within a process share the same <see cref="AsyncLocal{T}"/> slot.
    /// </summary>
    public static AsyncLocalCurrentTenantAccessor Instance { get; } = new();

    private readonly AsyncLocal<TenantInformation?> _currentScope;

    private AsyncLocalCurrentTenantAccessor()
    {
        _currentScope = new();
    }

    /// <inheritdoc/>
    public TenantInformation? Current
    {
        get => _currentScope.Value;
        set => _currentScope.Value = value;
    }
}
