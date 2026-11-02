// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Applies the configured score, action, and hostname checks to a token Google accepted. Google's <c>success</c>
/// only says the token is valid for the site key; for v3 a bot gets a valid token with a low score, and a token minted
/// on one page verifies on any other, so <see cref="CaptchaVerifyResult.Success"/> must include these checks.
/// </summary>
internal static class ReCaptchaVerdictPolicy
{
    /// <summary>Returns the policy error codes the accepted token fails, or an empty array when it passes.</summary>
    public static string[] Evaluate(ReCaptchaOptions options, string? hostName, string? action, float? score, bool isV3)
    {
        List<string>? failures = null;

        // A v3 response with no score cannot prove the threshold, so it fails rather than reading as 0-and-allowed.
        if (isV3 && (score is null || score.Value < options.MinimumScore))
        {
            (failures ??= []).Add(ReCaptchaErrorExtensions.ScoreTooLowCode);
        }

        if (
            isV3
            && options.ExpectedAction is not null
            && !string.Equals(action, options.ExpectedAction, StringComparison.Ordinal)
        )
        {
            (failures ??= []).Add(ReCaptchaErrorExtensions.ActionMismatchCode);
        }

        if (
            options.ExpectedHostName is not null
            && !string.Equals(hostName, options.ExpectedHostName, StringComparison.OrdinalIgnoreCase)
        )
        {
            (failures ??= []).Add(ReCaptchaErrorExtensions.HostNameMismatchCode);
        }

        return failures is null ? [] : [.. failures];
    }
}
