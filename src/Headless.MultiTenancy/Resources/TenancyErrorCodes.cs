// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy.Resources;

/// <summary>
/// Compile-time constants for tenant-catalog <c>errors[].code</c> values emitted in framework
/// <c>ProblemDetails</c> responses. All codes follow the framework-standard <c>g:snake_case</c> shape
/// (the <c>g:</c> prefix marks the shared "general" descriptor space). Clients should branch on these
/// constants rather than inspect the human-readable description, which is localized.
/// </summary>
/// <remarks>
/// <see cref="ResolutionFailed"/> is the secure-by-default code for unknown, disabled, and
/// claim-mismatch outcomes — it is what callers see unless
/// <see cref="TenantCatalogOptions.DetailedResolutionErrors"/> is enabled. The granular codes
/// (<see cref="Unknown"/>, <see cref="Disabled"/>, <see cref="IdentifierMismatch"/>) surface only
/// under that opt-in diagnostics option, so a tenant directory cannot be enumerated from response
/// differences by default. <see cref="IdentifierInvalid"/> always keeps its own code and 400 status:
/// shape validation reveals nothing tenant-specific.
/// </remarks>
[PublicAPI]
public static class TenancyErrorCodes
{
    /// <summary>
    /// Secure-by-default rejection shared by unknown, disabled, and claim-mismatch outcomes. Maps to 404.
    /// </summary>
    public const string ResolutionFailed = "g:tenant_resolution_failed";

    /// <summary>The identifier has no matching catalog row. Surfaced only under the diagnostics option. Maps to 404.</summary>
    public const string Unknown = "g:tenant_unknown";

    /// <summary>The identifier resolved to a disabled tenant. Surfaced only under the diagnostics option. Maps to 403.</summary>
    public const string Disabled = "g:tenant_disabled";

    /// <summary>
    /// The identifier-resolved tenant does not match the authenticated tenant claim. Surfaced only
    /// under the diagnostics option. Maps to 403.
    /// </summary>
    public const string IdentifierMismatch = "g:tenant_identifier_mismatch";

    /// <summary>The identifier failed shape validation before any cache or store lookup. Maps to 400.</summary>
    public const string IdentifierInvalid = "g:tenant_identifier_invalid";
}
