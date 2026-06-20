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
        return new(code: "g:concurrency_failure", description: Messages.g_concurrency_failure);
    }

    public static ErrorDescriptor DuplicatedRequest()
    {
        return new(code: "g:duplicated_request", description: Messages.g_duplicated_request);
    }

    public static ErrorDescriptor UnknownError()
    {
        return new(code: "g:unknown_error", description: Messages.g_unknown_error);
    }

    public static ErrorDescriptor ObsoleteApi()
    {
        return new(code: "g:obsolete_api", description: Messages.g_obsolete_api);
    }

    public static ErrorDescriptor NotAuthorized()
    {
        return new ErrorDescriptor(code: "g:not_authorized", description: Messages.g_not_authorized);
    }

    public static ErrorDescriptor FeatureCurrentlyUnavailable()
    {
        return new(code: "g:feature_currently_not_available", description: Messages.g_feature_currently_unavailable);
    }

    public static ErrorDescriptor UserNotFound()
    {
        return new ErrorDescriptor(code: "g:user_not_found", description: Messages.g_user_not_found);
    }

    public static ErrorDescriptor InvalidRecaptcha()
    {
        return new ErrorDescriptor(code: "g:invalid_recaptcha", description: Messages.g_invalid_recaptcha);
    }
}
