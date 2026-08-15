// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using FluentValidation;
using Headless.Constants;

namespace Headless.MultiTenancy;

/// <summary>Options controlling tenant catalog caching, identifier shape, and diagnostics.</summary>
[PublicAPI]
public sealed class TenantCatalogOptions
{
    /// <summary>
    /// How long a resolved identifier→id mapping and an id→<see cref="TenantInfo"/> entry stay cached
    /// before the catalog service re-reads the store. Bounds staleness: a disable or metadata change
    /// propagates within this window. Default: 5 minutes.
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long an unknown identifier is cached as a negative entry, so repeated probes of the same
    /// unknown identifier do not reach the store. A newly created tenant becomes resolvable within this
    /// window. <see cref="TimeSpan.Zero"/> disables negative caching. Default: 30 seconds.
    /// </summary>
    public TimeSpan UnknownIdentifierCacheExpiration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Identifiers that end resolution with no store call and no tenant (for example <c>www</c>,
    /// <c>api</c>). Compared case-insensitively against the already-normalized (trimmed, lowercased)
    /// identifier — casing in this list does not matter.
    /// </summary>
    public IList<string> IgnoredIdentifiers { get; set; } = [];

    /// <summary>The maximum accepted length, in characters, of a normalized identifier. Default: 63 (the DNS-label limit).</summary>
    public int MaxIdentifierLength { get; set; } = 63;

    /// <summary>
    /// The shape a normalized identifier must match before any cache or store lookup. Default:
    /// <see cref="RegexPatterns.Slug"/> — lowercase letters, digits, and single hyphens between
    /// segments (DNS-label form), which matches R21's default shape after normalization.
    /// </summary>
    public Regex IdentifierPattern { get; set; } = RegexPatterns.Slug;

    /// <summary>
    /// When <see langword="true"/>, resolution failures surface granular <c>g:</c> error codes and
    /// HTTP statuses (unknown 404, disabled 403, mismatch 403) instead of the secure-by-default
    /// generic rejection (<c>g:tenant_resolution_failed</c>, 404) shared by unknown, disabled, and
    /// claim-mismatch outcomes. Intended for development and trusted environments only.
    /// Default: <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The default collapse buys exactly one guarantee: the three rejection outcomes are mutually
    /// indistinguishable, so a caller that is refused cannot tell whether the identifier is unknown,
    /// belongs to a disabled tenant, or conflicts with its own tenant claim. It does not hide the
    /// existence of an <em>enabled</em> tenant — that request proceeds to the endpoint and returns the
    /// application's own status, which already differs from the 404 an unknown identifier receives.
    /// Enabling this option gives up the rejection-indistinguishability guarantee only.
    /// </remarks>
    public bool DetailedResolutionErrors { get; set; }
}

/// <summary>Validator for <see cref="TenantCatalogOptions"/>.</summary>
internal sealed class TenantCatalogOptionsValidator : AbstractValidator<TenantCatalogOptions>
{
    public TenantCatalogOptionsValidator()
    {
        RuleFor(x => x.CacheExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.UnknownIdentifierCacheExpiration).GreaterThanOrEqualTo(TimeSpan.Zero);
        RuleFor(x => x.MaxIdentifierLength).GreaterThan(0);
        RuleFor(x => x.IdentifierPattern).NotNull();
        RuleForEach(x => x.IgnoredIdentifiers).NotEmpty();
    }
}
