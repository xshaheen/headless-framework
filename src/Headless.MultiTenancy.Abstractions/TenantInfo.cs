// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.MultiTenancy;

/// <summary>
/// Canonical tenant metadata served by the tenant catalog. Non-generic and non-sealed: the catalog
/// pipeline (store SPI, resolution, caching, ambient context) carries no type parameter for this
/// model. Apps that need additional typed fields subclass <see cref="TenantInfo"/> from an app-owned
/// <see cref="ITenantStore"/> implementation and read it back through a registered typed leaf
/// accessor — no other pipeline surface becomes generic just to carry extra columns.
/// </summary>
/// <remarks>
/// Per-tenant application configuration does not belong on this model or in <see cref="ExtraProperties"/>:
/// it belongs in Settings/Features/Permissions keyed by <see cref="Id"/>. This model owns tenant
/// identity, routing (<see cref="Identifier"/>), and lifecycle (<see cref="IsEnabled"/>) only.
/// </remarks>
[PublicAPI]
public class TenantInfo : IHasExtraProperties
{
    /// <summary>Initializes a new <see cref="TenantInfo"/>.</summary>
    /// <param name="id">The canonical tenant identifier used for persistence, authorization, and ambient context.</param>
    /// <param name="identifier">The public-facing tenant identifier (for example a subdomain or slug) that maps to <paramref name="id"/>.</param>
    /// <param name="name">The tenant's display name, or <see langword="null"/> when not set.</param>
    /// <param name="isEnabled">Whether the tenant is currently enabled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> or <paramref name="identifier"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="identifier"/> is empty or white space.</exception>
    public TenantInfo(string id, string identifier, string? name, bool isEnabled)
    {
        Id = Argument.IsNotNullOrWhiteSpace(id);
        Identifier = Argument.IsNotNullOrWhiteSpace(identifier);
        Name = name;
        IsEnabled = isEnabled;
    }

    /// <summary>
    /// The canonical tenant identifier used for persistence, authorization, and ambient
    /// <see cref="ICurrentTenant.Id"/>. Stable across identifier rebrands.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The public-facing tenant identifier (for example a subdomain or slug) that maps to <see cref="Id"/>.
    /// This value is already normalized (trimmed, lowercased) — the catalog service owns normalization
    /// and stores compare it ordinally without re-normalizing.
    /// </summary>
    public string Identifier { get; }

    /// <summary>The tenant's display name, or <see langword="null"/> when not set.</summary>
    public string? Name { get; }

    /// <summary>
    /// Whether the tenant is currently enabled. A disabled tenant still reads through the tenant-info
    /// accessor — rejection on disablement is a resolution-time concern only, never an accessor concern.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Read-along payload for the tenant. Never participates in lookups or caching keys. Per-tenant
    /// application configuration belongs in Settings/Features/Permissions keyed by <see cref="Id"/>,
    /// not here.
    /// </summary>
    public ExtraProperties ExtraProperties { get; init; } = [];
}
