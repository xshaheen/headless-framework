// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.MultiTenancy.Resources;

/// <summary>
/// Factory methods that create <see cref="ErrorDescriptor"/> instances for tenant-catalog resolution
/// failures. Codes follow the <c>g:snake_case</c> shape.
/// </summary>
[PublicAPI]
public static class TenancyMessageDescriber
{
    /// <summary>
    /// Returns the secure-by-default descriptor shared by unknown, disabled, and claim-mismatch
    /// outcomes (<c>g:tenant_resolution_failed</c>).
    /// </summary>
    public static ErrorDescriptor ResolutionFailed()
    {
        return new(code: TenancyErrorCodes.ResolutionFailed, description: Messages.g_tenant_resolution_failed);
    }

    /// <summary>
    /// Returns a descriptor indicating the identifier has no matching catalog row (<c>g:tenant_unknown</c>).
    /// Surfaced only when diagnostics are enabled.
    /// </summary>
    public static ErrorDescriptor Unknown()
    {
        return new(code: TenancyErrorCodes.Unknown, description: Messages.g_tenant_unknown);
    }

    /// <summary>
    /// Returns a descriptor indicating the identifier resolved to a disabled tenant (<c>g:tenant_disabled</c>).
    /// Surfaced only when diagnostics are enabled.
    /// </summary>
    public static ErrorDescriptor Disabled()
    {
        return new(code: TenancyErrorCodes.Disabled, description: Messages.g_tenant_disabled);
    }

    /// <summary>
    /// Returns a descriptor indicating the identifier-resolved tenant does not match the authenticated
    /// tenant claim (<c>g:tenant_identifier_mismatch</c>). Surfaced only when diagnostics are enabled.
    /// </summary>
    public static ErrorDescriptor IdentifierMismatch()
    {
        return new(code: TenancyErrorCodes.IdentifierMismatch, description: Messages.g_tenant_identifier_mismatch);
    }

    /// <summary>
    /// Returns a descriptor indicating the identifier failed shape validation before any cache or
    /// store lookup (<c>g:tenant_identifier_invalid</c>).
    /// </summary>
    public static ErrorDescriptor IdentifierInvalid()
    {
        return new(code: TenancyErrorCodes.IdentifierInvalid, description: Messages.g_tenant_identifier_invalid);
    }
}
