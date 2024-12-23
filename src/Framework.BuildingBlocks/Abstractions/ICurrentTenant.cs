// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Models.Primitives;
using Framework.Core;

namespace Framework.BuildingBlocks.Abstractions;

public interface ICurrentTenant
{
    bool IsAvailable { get; }

    string? Id { get; }

    string? Name { get; }

    IDisposable Change(string? id, string? name = null);
}

public sealed class NullCurrentTenant : ICurrentTenant
{
    public bool IsAvailable => false;

    public string? Id => null;

    public string? Name => null;

    public IDisposable Change(string? id, string? name = null) => NullDisposable.Instance;
}

public sealed class CurrentTenant(ICurrentTenantAccessor currentTenantAccessor) : ICurrentTenant
{
    public bool IsAvailable => Id is not null;

    public string? Id => currentTenantAccessor.Current?.TenantId;

    public string? Name => currentTenantAccessor.Current?.Name;

    public IDisposable Change(string? id, string? name = null) => _SetCurrent(id, name);

    private IDisposable _SetCurrent(string? tenantId, string? name = null)
    {
        var currentScope = currentTenantAccessor.Current;

        currentTenantAccessor.Current = new TenantInformation(tenantId, name);

        // Reset on dispose
        return Disposable.Create(
            (currentTenantAccessor, currentScope),
            static state =>
            {
                var (currentTenantAccessor, currentScope) = state;

                currentTenantAccessor.Current = currentScope;
            }
        );
    }
}
