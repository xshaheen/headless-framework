// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>Error codes returned by the Google reCAPTCHA siteverify API.</summary>
/// <remarks>
/// New members may be added in minor versions as Google introduces additional error codes. Consumers that
/// <see langword="switch"/> on this enum must always handle <see cref="Unknown"/> / the <see langword="default"/> case so a newly added
/// member degrades to "treat as unknown" rather than falling through unhandled.
/// </remarks>
[PublicAPI]
public enum ReCaptchaError
{
    /// <summary>An unknown or unrecognized error code was returned by the API.</summary>
    Unknown = -1,

    /// <summary>The secret parameter is missing.</summary>
    MissingInputSecret = 0,

    /// <summary>The secret parameter is invalid or malformed.</summary>
    InvalidInputSecret = 1,

    /// <summary>The response parameter is missing.</summary>
    MissingInputResponse = 2,

    /// <summary>The response parameter is invalid or malformed.</summary>
    InvalidInputResponse = 3,

    /// <summary>The request is invalid or malformed.</summary>
    BadRequest = 4,

    /// <summary>The response is no longer valid: either is too old or has been used previously.</summary>
    TimeOutOrDuplicate = 5,
}

/// <summary>Helpers for converting Google's raw error-code strings into <see cref="ReCaptchaError"/> values.</summary>
[PublicAPI]
public static class ReCaptchaErrorCodesExtensions
{
    /// <summary>
    /// Maps Google's <c>error-codes</c> strings to <see cref="ReCaptchaError"/> values; unrecognized codes
    /// map to <see cref="ReCaptchaError.Unknown"/>. A <see langword="null"/> or empty input yields an empty list.
    /// </summary>
    /// <param name="errorCodes">The raw error-code strings, typically <c>response.ErrorCodes</c>.</param>
    /// <returns>The parsed error values, in input order (never <see langword="null"/>).</returns>
    public static IReadOnlyList<ReCaptchaError> ToReCaptchaErrors(this IReadOnlyList<string>? errorCodes)
    {
        if (errorCodes is null || errorCodes.Count == 0)
        {
            return [];
        }

        var errors = new ReCaptchaError[errorCodes.Count];

        for (var i = 0; i < errorCodes.Count; i++)
        {
            errors[i] = errorCodes[i].ToReCaptchaError();
        }

        return errors;
    }
}
