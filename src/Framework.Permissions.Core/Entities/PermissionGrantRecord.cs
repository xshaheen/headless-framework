// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
using Framework.Domains;

namespace Framework.Permissions.Entities;

public sealed class PermissionGrantRecord : AggregateRoot<Guid>, IMultiTenant
{
    public string Name { get; private init; }

    public string ProviderName { get; private init; }

    public string ProviderKey { get; private init; }

    public string? TenantId { get; private init; }

    [UsedImplicitly]
    private PermissionGrantRecord()
    {
        Name = null!;
        ProviderName = null!;
        ProviderKey = null!;
    }

    [SetsRequiredMembers]
    public PermissionGrantRecord(Guid id, string name, string providerName, string providerKey, string? tenantId = null)
    {
        Id = id;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        ProviderKey = Argument.IsNotNullOrWhiteSpace(providerKey);
        TenantId = tenantId;
    }
}
