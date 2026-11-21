// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;

namespace Headless.Features;

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
        ConcurrencyStamp = NewConcurrencyStamp();
    }

    /// <summary>Creates a <see cref="FeatureValueRecord"/> from raw storage data, including audit timestamps.</summary>
    /// <param name="id">The unique identifier for this record.</param>
    /// <param name="name">The name of the feature.</param>
    /// <param name="value">The stored feature value.</param>
    /// <param name="providerName">The name of the value provider that owns this record.</param>
    /// <param name="providerKey">The provider-specific scoping key, or <see langword="null"/>.</param>
    /// <param name="createdAt">UTC timestamp when the record was first created.</param>
    /// <param name="updatedAt">UTC timestamp of the last update, or <see langword="null"/> if never updated.</param>
    /// <param name="concurrencyStamp">
    /// The stored stamp, or <see langword="null"/> for a row written before the store kept one.
    /// </param>
    /// <returns>A fully-hydrated <see cref="FeatureValueRecord"/> with audit fields populated.</returns>
    public static FeatureValueRecord FromStorage(
        Guid id,
        string name,
        string value,
        string providerName,
        string? providerKey,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt,
        string? concurrencyStamp = null
    )
    {
        return new FeatureValueRecord(id, name, value, providerName, providerKey)
        {
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            ConcurrencyStamp = concurrencyStamp,
        };
    }

    /// <summary>The name of the feature whose value is stored.</summary>
    public string Name { get; private init; }

    /// <summary>The stored value for the feature.</summary>
    public string Value { get; private set; }

    /// <summary>
    /// Gets the token that changes on every write of this record, or <see langword="null"/> for a row written before
    /// the store kept one. A versioned read hashes it into the group version, so any later save of the row, even one
    /// that stores the same value, makes that version stale.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="IHasConcurrencyStamp"/>: that would make it an EF Core concurrency token on every
    /// update, and the unversioned writes are last-writer-wins by contract. A versioned write checks the stamps of its
    /// whole group inside one serializable transaction instead.
    /// </remarks>
    public string? ConcurrencyStamp { get; private set; }

    /// <summary>The name of the value provider that owns this record (e.g. <c>Tenant</c>, <c>Edition</c>).</summary>
    public string ProviderName { get; private init; }

    /// <summary>The provider-specific key that scopes this value (e.g. a tenant ID), or <see langword="null"/> for provider-global values.</summary>
    public string? ProviderKey { get; private init; }

    /// <summary>Gets the UTC timestamp when this record was first created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the UTC timestamp of the last update, or <see langword="null"/> if the record has never been updated.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Replaces the value and draws a new <see cref="ConcurrencyStamp"/>.</summary>
    internal void ChangeValue(string value)
    {
        Value = Argument.IsNotNullOrWhiteSpace(value);
        ConcurrencyStamp = NewConcurrencyStamp();
    }

    /// <summary>Returns a new random stamp, 32 hexadecimal characters.</summary>
    internal static string NewConcurrencyStamp() => Guid.NewGuid().ToString("N");

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{base.ToString()}, Name = {Name}, Value = {Value}, ProviderName = {ProviderName}, ProviderKey = {ProviderKey}";
    }
}
