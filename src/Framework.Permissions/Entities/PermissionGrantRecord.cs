// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
using Framework.Domains;

namespace Framework.Permissions.Entities;

public sealed class PermissionGrantRecord : AggregateRoot<Guid>, IMultiTenant<string?>
{
    public string Name { get; private set; }

    public string ProviderName { get; private set; }

    public string ProviderKey { get; private set; }

    public string? TenantId { get; private set; }

    private PermissionGrantRecord()
    {
        Name = default!;
        ProviderName = default!;
        ProviderKey = default!;
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
