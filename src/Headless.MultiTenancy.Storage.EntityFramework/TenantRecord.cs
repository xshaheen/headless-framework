// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Domain;
using Headless.Primitives;

namespace Headless.MultiTenancy;

/// <summary>
/// EF Core persistence entity backing the default <see cref="ITenantStore"/> shipped by this package —
/// a convenience default schema (KTD6), not a canonical one. Apps with richer requirements may implement
/// <see cref="ITenantStore"/> directly over their own aggregate instead of this entity.
/// </summary>
/// <remarks>
/// Deliberately does not implement <c>IMultiTenant</c>: the tenant catalog sits outside the EF tenant
/// query filter by construction — a tenant record is never itself scoped to a tenant. There is no
/// framework write path; apps that seed or rebrand rows go through <see cref="SetIdentifier"/> so
/// <see cref="NormalizedIdentifier"/> can never carry a stale or hand-written value.
/// </remarks>
public sealed class TenantRecord : AggregateRoot<string>, IHasExtraProperties
{
    /// <summary>Parameterless constructor for ORM/serializer use only.</summary>
    [SetsRequiredMembers]
    [UsedImplicitly]
    private TenantRecord()
    {
        Id = null!;
        Identifier = null!;
        NormalizedIdentifier = null!;
    }

    /// <summary>Initializes a new <see cref="TenantRecord"/>.</summary>
    /// <param name="id">The canonical tenant id. See <see cref="TenantInfo.Id"/>.</param>
    /// <param name="identifier">The public-facing tenant identifier. See <see cref="TenantInfo.Identifier"/>.</param>
    /// <param name="name">The tenant's display name, or <see langword="null"/> when not set.</param>
    /// <param name="isEnabled">Whether the tenant is enabled. Default: <see langword="true"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> or <paramref name="identifier"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="identifier"/> is empty or white space.</exception>
    [SetsRequiredMembers]
    public TenantRecord(string id, string identifier, string? name = null, bool isEnabled = true)
    {
        Id = Argument.IsNotNullOrWhiteSpace(id);
        Name = name;
        IsEnabled = isEnabled;
        SetIdentifier(identifier);
    }

    /// <summary>The public-facing tenant identifier, in the casing supplied by the caller (kept for display/audit).</summary>
    public string Identifier { get; private set; }

    /// <summary>
    /// The normalized (trimmed, lowercased) form of <see cref="Identifier"/>. Derived and kept in sync by
    /// <see cref="SetIdentifier"/> only — never independently settable. The unique lookup index and the EF
    /// store's identifier lookup both target this column.
    /// </summary>
    public string NormalizedIdentifier { get; private set; }

    /// <summary>The tenant's display name, or <see langword="null"/> when not set.</summary>
    public string? Name { get; set; }

    /// <summary>Whether the tenant is currently enabled.</summary>
    public bool IsEnabled { get; set; }

    /// <inheritdoc/>
    public ExtraProperties ExtraProperties { get; init; } = [];

    /// <summary>
    /// Sets <see cref="Identifier"/> and recomputes <see cref="NormalizedIdentifier"/> from it in the same
    /// operation, so the two columns can never drift — covers both the initial insert and an identifier
    /// rebrand. Normalization matches the catalog service's rule (trim, lowercase invariant).
    /// </summary>
    /// <param name="identifier">The new public-facing tenant identifier.</param>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="identifier"/> is empty or white space.</exception>
    [MemberNotNull(nameof(Identifier), nameof(NormalizedIdentifier))]
    public void SetIdentifier(string identifier)
    {
        Identifier = Argument.IsNotNullOrWhiteSpace(identifier);
        NormalizedIdentifier = Identifier.Trim().ToLowerInvariant();
    }
}
