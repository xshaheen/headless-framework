// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;
using Headless.MultiTenancy;
using Headless.Primitives;

namespace Headless.Abstractions;

/// <summary>
/// <see cref="ICurrentTenant"/> implementation backed by <see cref="ICurrentTenantAccessor"/>.
/// Reads and writes tenant context through the accessor, enabling AsyncLocal-scoped isolation
/// across async call chains.
/// </summary>
public sealed class CurrentTenant(ICurrentTenantAccessor currentTenantAccessor) : ICurrentTenant
{
    /// <inheritdoc/>
    public bool IsAvailable => Id is not null;

    /// <inheritdoc/>
    public string? Id => currentTenantAccessor.Current?.TenantId;

    /// <inheritdoc/>
    public string? Name => currentTenantAccessor.Current?.Name;

    /// <inheritdoc/>
    public IDisposable Change(string? id, string? name = null)
    {
        return _SetCurrent(id, name);
    }

    [MustDisposeResource]
    private IDisposable _SetCurrent(string? tenantId, string? name = null)
    {
        var currentScope = currentTenantAccessor.Current;

        currentTenantAccessor.Current = new TenantInformation(tenantId, name);

        // Tenant switching sits on per-request and per-message paths: the state-taking overload with a
        // static lambda keeps the reset to a single allocation, unlike a closure over the accessor.
        return DisposableFactory.Create(
            (Accessor: currentTenantAccessor, Previous: currentScope),
            static scope => scope.Accessor.Current = scope.Previous
        );
    }
}
