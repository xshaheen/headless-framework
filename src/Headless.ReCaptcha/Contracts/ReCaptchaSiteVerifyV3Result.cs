// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.ReCaptcha.Contracts;

/// <summary>Outcome of an enforced v3 verification (success + action match + score threshold).</summary>
public sealed class ReCaptchaSiteVerifyV3Result
{
    /// <summary>Whether the token passed all checks: successful, matching action, and score at or above the threshold.</summary>
    [MemberNotNullWhen(false, nameof(FailureReason))]
    public required bool IsValid { get; init; }

    /// <summary>The raw siteverify response that produced this result.</summary>
    public required ReCaptchaSiteVerifyV3Response Response { get; init; }

    /// <summary>The reason the verification failed, or <see langword="null"/> when <see cref="IsValid"/> is <see langword="true"/>.</summary>
    public ReCaptchaV3VerificationFailureReason? FailureReason { get; init; }
}

/// <summary>Why an enforced v3 verification failed.</summary>
public enum ReCaptchaV3VerificationFailureReason
{
    /// <summary>Google reported the token as not successful (see <see cref="ReCaptchaSiteVerifyV3Response.ErrorCodes"/>).</summary>
    Unsuccessful = 0,

    /// <summary>The response action did not match the expected action (possible cross-action token replay).</summary>
    ActionMismatch = 1,

    /// <summary>The score was missing or below the required threshold.</summary>
    ScoreBelowThreshold = 2,
}
