// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Recaptcha.Internals;

namespace Framework.Recaptcha.Contracts;

public sealed class ReCaptchaSiteVerifyV2Response
{
    /// <summary>Whether this request was a valid reCAPTCHA token for your site.</summary>
    [JsonPropertyName("success")]
    [MemberNotNullWhen(true, nameof(HostName), nameof(ChallengeTimeStamp))]
    [MemberNotNullWhen(false, nameof(ErrorCodes))]
    public bool Success { get; init; }

    /// <summary>Timestamp of the challenge load.</summary>
    [JsonPropertyName("challenge_ts")]
    public DateTime? ChallengeTimeStamp { get; init; }

    /// <summary>The hostname of the site where the reCAPTCHA was solved.</summary>
    [JsonPropertyName("hostname")]
    public string? HostName { get; init; }

    /// <summary>Error code if not <see cref="Success"/>.</summary>
    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; init; }

    public ReCaptchaError[] ParseErrors()
    {
        return ErrorCodes?.ConvertAll(ParseError) ?? [];
    }

    public static ReCaptchaError ParseError(string error)
    {
        return error.ToReCaptchaError();
    }
}
