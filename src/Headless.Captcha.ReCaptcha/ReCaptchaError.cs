// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>Represents error codes returned by the Google reCAPTCHA site verification API.</summary>
/// <remarks>
/// New members can be added when upstream error codes are introduced. Handle <see cref="Unknown"/> or the
/// <see langword="default"/> case to accommodate unrecognized values.
/// </remarks>
[PublicAPI]
public enum ReCaptchaError
{
    /// <summary>An unknown or unrecognized error code was returned by the API. Represents the default sentinel value.</summary>
    Unknown = 0,

    /// <summary>The secret parameter is missing.</summary>
    MissingInputSecret = 1,

    /// <summary>The secret parameter is invalid or malformed.</summary>
    InvalidInputSecret = 2,

    /// <summary>The response parameter is missing.</summary>
    MissingInputResponse = 3,

    /// <summary>The response parameter is invalid or malformed.</summary>
    InvalidInputResponse = 4,

    /// <summary>The request is invalid or malformed.</summary>
    BadRequest = 5,

    /// <summary>The response is no longer valid because it expired or was already used.</summary>
    TimeOutOrDuplicate = 6,
}

/// <summary>Provides extension methods for converting reCAPTCHA error code strings into <see cref="ReCaptchaError"/> values.</summary>
[PublicAPI]
public static class ReCaptchaErrorCodesExtensions
{
    /// <summary>
    /// Maps error code strings to <see cref="ReCaptchaError"/> values. Unrecognized codes map to
    /// <see cref="ReCaptchaError.Unknown"/>.
    /// </summary>
    /// <param name="errorCodes">The raw error code strings.</param>
    /// <returns>A read-only list of parsed error values in input order.</returns>
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
