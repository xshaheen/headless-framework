// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Resources;

/// <summary>
/// Compile-time constants for general cross-cutting <c>errors[].code</c> values emitted in
/// framework <c>ProblemDetails</c> responses. All codes follow the framework-standard
/// <c>g:snake_case</c> shape (the <c>g:</c> prefix marks the shared "general" descriptor space).
/// Clients should branch on these constants rather than inspect the human-readable description,
/// which is localized.
/// </summary>
[PublicAPI]
public static class GeneralErrorCodes
{
    /// <summary>Caller is authenticated but not permitted to perform the operation. Maps to 403.</summary>
    public const string Forbidden = "g:forbidden";

    /// <summary>
    /// The endpoint was configured with a request type that does not match the argument supplied to
    /// the validation filter. Maps to 400.
    /// </summary>
    public const string InvalidRequestType = "g:invalid_request_type";

    /// <summary>An optimistic-concurrency conflict prevented the operation from completing. Maps to 409.</summary>
    public const string ConcurrencyFailure = "g:concurrency_failure";

    /// <summary>The request was already processed or is a duplicate of an in-flight request. Maps to 409.</summary>
    public const string DuplicatedRequest = "g:duplicated_request";

    /// <summary>An unclassified server-side error occurred. Maps to 500.</summary>
    public const string UnknownError = "g:unknown_error";

    /// <summary>A deprecated endpoint was called. Maps to 400.</summary>
    public const string ObsoleteApi = "g:obsolete_api";

    /// <summary>The caller is unauthenticated or otherwise not authorized. Maps to 401/403.</summary>
    public const string NotAuthorized = "g:not_authorized";

    /// <summary>The requested feature is temporarily unavailable. Maps to 409.</summary>
    public const string FeatureCurrentlyNotAvailable = "g:feature_currently_not_available";

    /// <summary>The referenced user does not exist. Maps to 404/409.</summary>
    public const string UserNotFound = "g:user_not_found";

    /// <summary>The supplied reCAPTCHA token is missing or failed verification. Maps to 409.</summary>
    public const string InvalidRecaptcha = "g:invalid_recaptcha";
}
