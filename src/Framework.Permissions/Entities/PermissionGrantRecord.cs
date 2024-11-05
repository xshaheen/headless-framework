using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.Checks;
using Framework.Kernel.Domains;

namespace Framework.Permissions.Entities;

public sealed class PermissionGrantRecord : AggregateRoot<Guid>, IMultiTenant<Guid?>
{
    public string Name { get; private set; }

    public string ProviderName { get; private set; }

    public string? ProviderKey { get; private set; }

    public Guid? TenantId { get; private set; }

    private PermissionGrantRecord()
    {
        Name = default!;
        ProviderName = default!;
    }

    [SetsRequiredMembers]
    public PermissionGrantRecord(Guid id, string name, string providerName, string? providerKey, Guid? tenantId = null)
    {
        Id = id;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        ProviderKey = providerKey;
        TenantId = tenantId;
    }
}
