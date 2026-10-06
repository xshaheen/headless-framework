// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api.Resources;

/// <summary>
/// Factory methods that create <see cref="ErrorDescriptor"/> instances for general cross-cutting
/// error responses. Codes follow the <c>g:snake_case</c> shape.
/// </summary>
/// <remarks>
/// Every call returns a new descriptor whose description resolves under
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>. Call the factory per response;
/// a descriptor cached in a static keeps the culture of its first caller.
/// </remarks>
[PublicAPI]
public static class GeneralMessageDescriber
{
    /// <summary>Returns a descriptor for an optimistic-concurrency conflict (<c>g:concurrency_failure</c>).</summary>
    public static ErrorDescriptor ConcurrencyFailure()
    {
        return new(code: GeneralErrorCodes.ConcurrencyFailure, description: Messages.g_concurrency_failure);
    }

    /// <summary>Returns a descriptor for a missing required <c>If-Match</c> header.</summary>
    public static ErrorDescriptor IfMatchRequired() =>
        new(GeneralErrorCodes.IfMatchRequired, Messages.g_if_match_required);

    /// <summary>Returns a descriptor for an invalid <c>If-Match</c> header.</summary>
    public static ErrorDescriptor IfMatchInvalid() =>
        new(GeneralErrorCodes.IfMatchInvalid, Messages.g_if_match_invalid);

    /// <summary>Returns a descriptor for a duplicate or already-processed request (<c>g:duplicated_request</c>).</summary>
    public static ErrorDescriptor DuplicatedRequest()
    {
        return new(code: GeneralErrorCodes.DuplicatedRequest, description: Messages.g_duplicated_request);
    }

    /// <summary>Returns a descriptor for an unclassified server-side error (<c>g:unknown_error</c>).</summary>
    public static ErrorDescriptor UnknownError()
    {
        return new(code: GeneralErrorCodes.UnknownError, description: Messages.g_unknown_error);
    }

    /// <summary>Returns a descriptor for a call to a deprecated endpoint (<c>g:obsolete_api</c>).</summary>
    public static ErrorDescriptor ObsoleteApi()
    {
        return new(code: GeneralErrorCodes.ObsoleteApi, description: Messages.g_obsolete_api);
    }

    /// <summary>Returns a descriptor for an unauthenticated or unauthorized caller (<c>g:not_authorized</c>).</summary>
    public static ErrorDescriptor NotAuthorized()
    {
        return new ErrorDescriptor(code: GeneralErrorCodes.NotAuthorized, description: Messages.g_not_authorized);
    }

    /// <summary>Returns a descriptor for a request rejected by an HTTP rate limiter (<c>g:rate_limit_exceeded</c>).</summary>
    public static ErrorDescriptor RateLimitExceeded()
    {
        return new ErrorDescriptor(
            code: GeneralErrorCodes.RateLimitExceeded,
            description: Messages.g_rate_limit_exceeded
        );
    }

    /// <summary>Returns a descriptor for a temporarily unavailable feature (<c>g:feature_currently_not_available</c>).</summary>
    public static ErrorDescriptor FeatureCurrentlyUnavailable()
    {
        return new(
            code: GeneralErrorCodes.FeatureCurrentlyNotAvailable,
            description: Messages.g_feature_currently_unavailable
        );
    }

    /// <summary>Returns a descriptor indicating the referenced user does not exist (<c>g:user_not_found</c>).</summary>
    public static ErrorDescriptor UserNotFound()
    {
        return new ErrorDescriptor(code: GeneralErrorCodes.UserNotFound, description: Messages.g_user_not_found);
    }

    /// <summary>Returns a descriptor for a failed or missing reCAPTCHA token (<c>g:invalid_recaptcha</c>).</summary>
    public static ErrorDescriptor InvalidRecaptcha()
    {
        return new ErrorDescriptor(code: GeneralErrorCodes.InvalidRecaptcha, description: Messages.g_invalid_recaptcha);
    }

    /// <summary>
    /// Returns a descriptor for an endpoint whose validation filter received no argument of its
    /// configured request type (<c>g:invalid_request_type</c>).
    /// </summary>
    public static ErrorDescriptor InvalidRequestType()
    {
        return new(code: GeneralErrorCodes.InvalidRequestType, description: Messages.g_invalid_request_type);
    }

    /// <summary>
    /// Returns a descriptor for an operation that required an ambient tenant context when none was set
    /// (<c>g:tenant_required</c>).
    /// </summary>
    public static ErrorDescriptor TenantRequired()
    {
        return new(code: GeneralErrorCodes.TenantRequired, description: Messages.g_tenant_required);
    }

    /// <summary>
    /// Returns a descriptor for a tenant-owned write that does not match the current tenant context
    /// (<c>g:cross_tenant_write</c>).
    /// </summary>
    public static ErrorDescriptor CrossTenantWrite()
    {
        return new(code: GeneralErrorCodes.CrossTenantWrite, description: Messages.g_cross_tenant_write);
    }
}
