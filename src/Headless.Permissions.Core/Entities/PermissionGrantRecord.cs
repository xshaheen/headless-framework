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
/// <remarks>
/// Unlike <c>SettingValueRecord</c> and <c>FeatureValueRecord</c> — which scope tenancy through
/// <see cref="ProviderName"/>/<see cref="ProviderKey"/> and have no first-class tenant column — permission
/// grants carry a first-class <see cref="TenantId"/> and implement <see cref="IMultiTenant"/>. This is an
/// intentional divergence, not drift: grants require tenant-scoped uniqueness expressed directly in the
/// (<see cref="Name"/>, <see cref="ProviderName"/>, <see cref="ProviderKey"/>, <see cref="TenantId"/>) unique
/// index, so the same (name, provider, key) grant can coexist independently per tenant and for the host.
/// </remarks>
public sealed class PermissionGrantRecord : AggregateRoot<Guid>, IMultiTenant, ICreateAudit, IUpdateAudit
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

    /// <summary>Gets the UTC timestamp when this grant record was first created.</summary>
    public DateTimeOffset DateCreated { get; private set; }

    /// <summary>Gets the UTC timestamp of the last update, or <see langword="null"/> if the record has never been updated.</summary>
    public DateTimeOffset? DateUpdated { get; private set; }

    /// <summary>Parameterless constructor for ORM/serializer use only.</summary>
    [UsedImplicitly]
    private PermissionGrantRecord()
    {
        Name = null!;
        ProviderName = null!;
        ProviderKey = null!;
    }

    /// <summary>Initializes a new <see cref="PermissionGrantRecord"/>.</summary>
    /// <param name="id">Unique identifier for the record.</param>
    /// <param name="name">Permission name. Must not be <see langword="null"/>, empty, or white space.</param>
    /// <param name="providerName">Grant provider name. Must not be <see langword="null"/>, empty, or white space.</param>
    /// <param name="providerKey">Provider-specific subject key. Must not be <see langword="null"/>, empty, or white space.</param>
    /// <param name="isGranted"><see langword="true"/> for a grant; <see langword="false"/> for an explicit denial.</param>
    /// <param name="tenantId">Optional tenant scope; <see langword="null"/> for host-level grants.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/>, <paramref name="providerName"/>, or <paramref name="providerKey"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/>, <paramref name="providerName"/>, or <paramref name="providerKey"/> is empty or white space.
    /// </exception>
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

    /// <summary>
    /// Creates a <see cref="PermissionGrantRecord"/> from raw storage data, including audit timestamps.
    /// </summary>
    /// <param name="id">Unique identifier for the record.</param>
    /// <param name="name">Permission name.</param>
    /// <param name="providerName">Grant provider name.</param>
    /// <param name="providerKey">Provider-specific subject key.</param>
    /// <param name="isGranted"><see langword="true"/> for a grant; <see langword="false"/> for an explicit denial.</param>
    /// <param name="tenantId">Optional tenant scope; <see langword="null"/> for host-level grants.</param>
    /// <param name="dateCreated">UTC timestamp when the record was first created.</param>
    /// <param name="dateUpdated">UTC timestamp of the last update, or <see langword="null"/> if never updated.</param>
    /// <returns>A fully-hydrated <see cref="PermissionGrantRecord"/> with audit fields populated.</returns>
    public static PermissionGrantRecord FromStorage(
        Guid id,
        string name,
        string providerName,
        string providerKey,
        bool isGranted,
        string? tenantId,
        DateTimeOffset dateCreated,
        DateTimeOffset? dateUpdated
    )
    {
        return new PermissionGrantRecord(id, name, providerName, providerKey, isGranted, tenantId)
        {
            DateCreated = dateCreated,
            DateUpdated = dateUpdated,
        };
    }
}
