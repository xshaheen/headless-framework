// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;

namespace Headless.Permissions.Entities;

/// <summary>
/// Aggregate root representing a persisted permission grant (or explicit denial) for a single principal.
/// The (Name, ProviderName, ProviderKey, TenantId) tuple uniquely identifies one row in the grant table.
/// <para>
/// Absence of a record means the permission is undefined for that principal. A record with
/// <see cref="IsGranted"/> = <see langword="false"/> is an explicit denial (AWS IAM-style).
/// </para>
/// </summary>
public sealed class PermissionGrantRecord : AggregateRoot<Guid>, IMultiTenant
{
    /// <summary>The name of the permission being granted or denied.</summary>
    public string Name { get; private init; }

    /// <summary>
    /// The short type name of the grant provider that owns this record (e.g. <c>R</c> for role,
    /// <c>U</c> for user). Matches the key used by the corresponding <c>IPermissionGrantProvider</c>.
    /// </summary>
    public string ProviderName { get; private init; }

    /// <summary>
    /// The provider-specific subject identifier (e.g. a role name, a user id). Together with
    /// <see cref="ProviderName"/> it identifies the principal this grant applies to.
    /// </summary>
    public string ProviderKey { get; private init; }

    /// <summary>
    /// The tenant this grant belongs to, or <see langword="null"/> for host-level (cross-tenant) grants.
    /// Isolates grant records when the application runs in a multi-tenant topology.
    /// </summary>
    public string? TenantId { get; private init; }

    /// <summary>
    /// Indicates whether this record represents a grant (true) or explicit denial (false).
    /// Absence of record = undefined, record with IsGranted=false = explicit deny (AWS IAM-style).
    /// </summary>
    public bool IsGranted { get; private init; }

    [UsedImplicitly]
    private PermissionGrantRecord()
    {
        Name = null!;
        ProviderName = null!;
        ProviderKey = null!;
    }

    [SetsRequiredMembers]
    public PermissionGrantRecord(
        Guid id,
        string name,
        string providerName,
        string providerKey,
        bool isGranted,
        string? tenantId = null
    )
    {
        Id = id;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        ProviderKey = Argument.IsNotNullOrWhiteSpace(providerKey);
        IsGranted = isGranted;
        TenantId = tenantId;
    }
}
