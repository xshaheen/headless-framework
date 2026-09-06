// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Primitives;

namespace Headless.Abstractions;

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
