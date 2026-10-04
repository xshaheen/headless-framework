// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Represents the normalized verification outcome shared across providers. Contains common pass or fail state and
/// shared fields.
/// </summary>
/// <remarks>
/// Properties remain nullable because providers do not guarantee <see cref="HostName"/> or
/// <see cref="ChallengeTimestamp"/> on success, nor <see cref="ErrorCodes"/> on failure.
/// </remarks>
[PublicAPI]
public class CaptchaVerifyResult
{
    /// <summary>Gets a value indicating whether the token is a valid CAPTCHA response for this site.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the timestamp when the challenge loaded, when returned by the provider.</summary>
    public DateTimeOffset? ChallengeTimestamp { get; init; }

    /// <summary>Gets the hostname of the site where the challenge was solved, when returned by the provider.</summary>
    public string? HostName { get; init; }

    /// <summary>Gets the action name associated with the request, when returned by the provider.</summary>
    public string? Action { get; init; }

    /// <summary>Gets provider error codes, when returned by the provider.</summary>
    public IReadOnlyList<string>? ErrorCodes { get; init; }
}
