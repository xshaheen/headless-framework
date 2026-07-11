// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api.Resources;

/// <summary>
/// Factory methods that create <see cref="ErrorDescriptor"/> instances for general cross-cutting
/// error responses. Codes follow the <c>g:snake_case</c> shape.
/// </summary>
[PublicAPI]
public static class GeneralMessageDescriber
{
    /// <summary>Returns a descriptor for an optimistic-concurrency conflict (<c>g:concurrency_failure</c>).</summary>
    public static ErrorDescriptor ConcurrencyFailure()
    {
        return new(code: GeneralErrorCodes.ConcurrencyFailure, description: Messages.g_concurrency_failure);
    }

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
}
