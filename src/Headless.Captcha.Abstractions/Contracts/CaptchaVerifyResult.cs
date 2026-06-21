// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// The normalized verification outcome shared by every provider. Stays strictly pass/fail plus the fields all
/// providers return; provider-only fields live on derived result types (<c>ReCaptchaV3VerifyResult.Score</c>,
/// <c>TurnstileVerifyResult.CData</c>). A consumer that reads provider-only data is, by definition, writing
/// provider-specific code and should resolve the provider's concrete verifier/result.
/// </summary>
[PublicAPI]
public class CaptchaVerifyResult
{
    /// <summary>Whether the token was a valid CAPTCHA response for this site.</summary>
    [MemberNotNullWhen(true, nameof(HostName), nameof(ChallengeTimestamp))]
    [MemberNotNullWhen(false, nameof(ErrorCodes))]
    public bool Success { get; init; }

    /// <summary>The timestamp of the challenge load, when the verification succeeded.</summary>
    public DateTimeOffset? ChallengeTimestamp { get; init; }

    /// <summary>The hostname of the site where the challenge was solved, when the verification succeeded.</summary>
    public string? HostName { get; init; }

    /// <summary>The action name associated with the request, when the provider returns one.</summary>
    public string? Action { get; init; }

    /// <summary>The provider error codes, when the verification failed.</summary>
    public string[]? ErrorCodes { get; init; }
}
