// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>
/// How pre-authentication tenant catalog resolution reads the tenant from a request in this application: the
/// identifier sources it consults and whether its rejections name the exact reason, so API documentation can describe
/// the resolution without depending on the types that implement it.
/// </summary>
/// <remarks>
/// <c>ResolveFromCatalog()</c> registers it as a singleton computed from the registrations, not from the request
/// pipeline, so a document generated without running the pipeline, such as at build time, describes the same contract.
/// Headless.OpenApi reads it to document the tenant headers, the tenant host servers, and the tenant rejections.
/// </remarks>
/// <param name="isConfigured">Whether resolution runs for the application's requests.</param>
/// <param name="detailedResolutionErrors">Whether rejections carry the granular error codes.</param>
/// <param name="headerNames">The distinct request headers the header source reads. Must not be <see langword="null"/>.</param>
/// <param name="hostTemplates">The host templates the host source matches. Must not be <see langword="null"/>.</param>
/// <param name="hasNonHeaderSource">Whether a source other than the header source can supply the identifier.</param>
[PublicAPI]
public sealed class TenantCatalogResolutionDescription(
    bool isConfigured,
    bool detailedResolutionErrors,
    IReadOnlyList<string> headerNames,
    IReadOnlyList<string> hostTemplates,
    bool hasNonHeaderSource
)
{
    /// <summary>
    /// Whether resolution runs for the application's requests: the tenant posture records the catalog resolution
    /// capability and at least one identifier source is registered. When <see langword="false"/>, the middleware
    /// resolves nothing, and the description <c>ResolveFromCatalog()</c> registers leaves every other member empty or
    /// <see langword="false"/>.
    /// </summary>
    public bool IsConfigured { get; } = isConfigured;

    /// <summary>
    /// Whether rejections carry the granular codes for an unknown, disabled, or mismatched tenant instead of one
    /// collapsed not-found code.
    /// </summary>
    public bool DetailedResolutionErrors { get; } = detailedResolutionErrors;

    /// <summary>The distinct request headers the header source reads, empty when no header source is registered.</summary>
    public IReadOnlyList<string> HeaderNames { get; } = Argument.IsNotNull(headerNames);

    /// <summary>The host templates the host source matches, empty when no host source is registered.</summary>
    public IReadOnlyList<string> HostTemplates { get; } = Argument.IsNotNull(hostTemplates);

    /// <summary>Whether a source other than the header source can supply the identifier.</summary>
    public bool HasNonHeaderSource { get; } = hasNonHeaderSource;
}
