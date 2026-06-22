// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

[PublicAPI]
public static class ReCaptchaResultExtensions
{
    /// <summary>Parses the result's raw <see cref="CaptchaVerifyResult.ErrorCodes"/> into the reCAPTCHA error enum.</summary>
    /// <param name="result">The verification result.</param>
    /// <returns>The parsed errors, or an empty array when the verification succeeded.</returns>
    public static ReCaptchaError[] ToReCaptchaErrors(this IReCaptchaVerifyResult result)
    {
        Argument.IsNotNull(result);

        var captchaResult = (CaptchaVerifyResult)result;

        return captchaResult.ErrorCodes is { } codes
            ? Array.ConvertAll(codes, static code => code.ToReCaptchaError())
            : [];
    }
}
