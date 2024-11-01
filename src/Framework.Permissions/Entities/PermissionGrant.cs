using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.Checks;
using Framework.Kernel.Domains;

namespace Framework.Permissions.Entities;

public sealed class PermissionGrant : AggregateRoot<Guid>, IMultiTenant<Guid?>
{
    public string Name { get; private set; } = default!;

    public string ProviderName { get; private set; } = default!;

    public string? ProviderKey { get; private set; }

    public Guid? TenantId { get; private set; }

    private PermissionGrant() { }

    [SetsRequiredMembers]
    public PermissionGrant(Guid id, string name, string providerName, string? providerKey, Guid? tenantId = null)
    {
        Id = id;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        ProviderKey = providerKey;
        TenantId = tenantId;
    }
}
