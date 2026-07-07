// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;

namespace Headless.Features.Entities;

/// <summary>Persistence record for a stored feature value, scoped to a specific provider and optional provider key.</summary>
/// <remarks>
/// Features scope tenancy through <see cref="ProviderName"/>/<see cref="ProviderKey"/> (with
/// <c>ProviderName == "Tenant"</c> and the tenant id in <see cref="ProviderKey"/>). It deliberately has
/// no first-class <c>TenantId</c> column nor <c>IMultiTenant</c> — a scoping value provider fully expresses
/// tenant, edition, and other scopes uniformly. This is an intentional divergence from
/// <c>PermissionGrantRecord</c>, not drift.
/// </remarks>
public sealed class FeatureValueRecord : AggregateRoot<Guid>, ICreateAudit, IUpdateAudit
{
    /// <summary>Parameterless constructor for ORM/serializer use only.</summary>
    [SetsRequiredMembers]
    [UsedImplicitly]
    private FeatureValueRecord()
    {
        Name = null!;
        Value = null!;
        ProviderName = null!;
    }

    /// <summary>Initializes a new <see cref="FeatureValueRecord"/>.</summary>
    /// <param name="id">The unique identifier for this record.</param>
    /// <param name="name">The name of the feature.</param>
    /// <param name="value">The stored feature value.</param>
    /// <param name="providerName">The name of the value provider that owns this record.</param>
    /// <param name="providerKey">The provider-specific scoping key, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="value"/>, or <paramref name="providerName"/> is <see langword="null"/> or white space.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/>, <paramref name="value"/>, or <paramref name="providerName"/> is empty or white space.</exception>
    [SetsRequiredMembers]
    public FeatureValueRecord(Guid id, string name, string value, string providerName, string? providerKey)
    {
        Id = id;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        Value = Argument.IsNotNullOrWhiteSpace(value);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        ProviderKey = providerKey;
    }

    /// <summary>Creates a <see cref="FeatureValueRecord"/> from raw storage data, including audit timestamps.</summary>
    /// <param name="id">The unique identifier for this record.</param>
    /// <param name="name">The name of the feature.</param>
    /// <param name="value">The stored feature value.</param>
    /// <param name="providerName">The name of the value provider that owns this record.</param>
    /// <param name="providerKey">The provider-specific scoping key, or <see langword="null"/>.</param>
    /// <param name="dateCreated">UTC timestamp when the record was first created.</param>
    /// <param name="dateUpdated">UTC timestamp of the last update, or <see langword="null"/> if never updated.</param>
    /// <returns>A fully-hydrated <see cref="FeatureValueRecord"/> with audit fields populated.</returns>
    public static FeatureValueRecord FromStorage(
        Guid id,
        string name,
        string value,
        string providerName,
        string? providerKey,
        DateTimeOffset dateCreated,
        DateTimeOffset? dateUpdated
    )
    {
        return new FeatureValueRecord(id, name, value, providerName, providerKey)
        {
            DateCreated = dateCreated,
            DateUpdated = dateUpdated,
        };
    }

    /// <summary>The name of the feature whose value is stored.</summary>
    public string Name { get; private init; }

    /// <summary>The stored value for the feature.</summary>
    public string Value { get; internal set; }

    /// <summary>The name of the value provider that owns this record (e.g. <c>Tenant</c>, <c>Edition</c>).</summary>
    public string ProviderName { get; private init; }

    /// <summary>The provider-specific key that scopes this value (e.g. a tenant ID), or <see langword="null"/> for provider-global values.</summary>
    public string? ProviderKey { get; private init; }

    /// <summary>Gets the UTC timestamp when this record was first created.</summary>
    public DateTimeOffset DateCreated { get; private set; }

    /// <summary>Gets the UTC timestamp of the last update, or <see langword="null"/> if the record has never been updated.</summary>
    public DateTimeOffset? DateUpdated { get; private set; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{base.ToString()}, Name = {Name}, Value = {Value}, ProviderName = {ProviderName}, ProviderKey = {ProviderKey}";
    }
}
