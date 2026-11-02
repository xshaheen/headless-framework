// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

internal static class ReCaptchaErrorExtensions
{
    // Codes for policy checks this library applies on top of Google's verdict. They follow Google's kebab-case shape
    // so ErrorCodes stays one list that ToReCaptchaErrors can parse.
    public const string ScoreTooLowCode = "score-too-low";
    public const string ActionMismatchCode = "action-mismatch";
    public const string HostNameMismatchCode = "hostname-mismatch";

    public static ReCaptchaError ToReCaptchaError(this string error)
    {
        return error switch
        {
            "bad-request" => ReCaptchaError.BadRequest,
            "timeout-or-duplicate" => ReCaptchaError.TimeOutOrDuplicate,
            "invalid-input-response" => ReCaptchaError.InvalidInputResponse,
            "missing-input-response" => ReCaptchaError.MissingInputResponse,
            "invalid-input-secret" => ReCaptchaError.InvalidInputSecret,
            "missing-input-secret" => ReCaptchaError.MissingInputSecret,
            ScoreTooLowCode => ReCaptchaError.ScoreTooLow,
            ActionMismatchCode => ReCaptchaError.ActionMismatch,
            HostNameMismatchCode => ReCaptchaError.HostNameMismatch,
            _ => ReCaptchaError.Unknown,
        };
    }
}
