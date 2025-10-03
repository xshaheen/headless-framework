// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;
using Framework.Primitives;

namespace Framework.Abstractions;

public interface ICurrentTenant
{
    bool IsAvailable { get; }

    string? Id { get; }

    string? Name { get; }

    [MustDisposeResource]
    IDisposable Change(string? id, string? name = null);
}

public sealed class NullCurrentTenant : ICurrentTenant
{
    public bool IsAvailable => false;

    public string? Id => null;

    public string? Name => null;

    public IDisposable Change(string? id, string? name = null) => DisposableFactory.Empty;
}

public sealed class CurrentTenant(ICurrentTenantAccessor currentTenantAccessor) : ICurrentTenant
{
    public bool IsAvailable => Id is not null;

    public string? Id => currentTenantAccessor.Current?.TenantId;

    public string? Name => currentTenantAccessor.Current?.Name;

    public IDisposable Change(string? id, string? name = null) => _SetCurrent(id, name);

    [MustDisposeResource]
    private IDisposable _SetCurrent(string? tenantId, string? name = null)
    {
        var currentScope = currentTenantAccessor.Current;

        currentTenantAccessor.Current = new TenantInformation(tenantId, name);

        // Reset on dispose
        return DisposableFactory.Create(() => currentTenantAccessor.Current = currentScope);
    }
}
