// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;

namespace Headless.ReCaptcha.Internals;

internal static class ReCaptchaErrorExtensions
{
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
            _ => ReCaptchaError.Unknown,
        };
    }
}
