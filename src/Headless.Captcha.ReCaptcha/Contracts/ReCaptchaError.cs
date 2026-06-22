// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>Error codes returned by the Google reCAPTCHA siteverify API.</summary>
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
    /// map to <see cref="ReCaptchaError.Unknown"/>. A <see langword="null"/> input yields an empty array.
    /// </summary>
    /// <param name="errorCodes">The raw error-code strings, typically <c>response.ErrorCodes</c>.</param>
    /// <returns>The parsed error values (never <see langword="null"/>).</returns>
    public static ReCaptchaError[] ToReCaptchaErrors(this string[]? errorCodes)
    {
        return errorCodes?.ConvertAll(static code => code.ToReCaptchaError()) ?? [];
    }
}
