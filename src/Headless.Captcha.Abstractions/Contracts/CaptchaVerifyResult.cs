// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// The normalized verification outcome shared by every provider. Stays strictly pass/fail plus the fields all
/// providers return; provider-only fields live on derived result types (<c>ReCaptchaV3VerifyResult.Score</c>,
/// <c>TurnstileVerifyResult.CData</c>). A consumer that reads provider-only data is, by definition, writing
/// provider-specific code and should resolve the provider's concrete verifier/result.
/// </summary>
/// <remarks>
/// The companion fields below are intentionally left as plain nullables with no <c>MemberNotNullWhen</c> flow
/// promise: vendors do not contractually guarantee <see cref="HostName"/>/<see cref="ChallengeTimestamp"/> on
/// success, nor <see cref="ErrorCodes"/> on failure, so the base type must not over-promise across providers.
/// </remarks>
[PublicAPI]
public class CaptchaVerifyResult
{
    /// <summary>Whether the token was a valid CAPTCHA response for this site.</summary>
    public bool Success { get; init; }

    /// <summary>The timestamp of the challenge load, when the provider returns one.</summary>
    public DateTimeOffset? ChallengeTimestamp { get; init; }

    /// <summary>The hostname of the site where the challenge was solved, when the provider returns one.</summary>
    public string? HostName { get; init; }

    /// <summary>The action name associated with the request, when the provider returns one.</summary>
    public string? Action { get; init; }

    /// <summary>The provider error codes, when the provider returns them (typically on failure).</summary>
    public IReadOnlyList<string>? ErrorCodes { get; init; }
}
