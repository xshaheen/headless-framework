// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Captcha;

/// <summary>Provides extension methods for reading reCAPTCHA data from an <see cref="IReCaptchaVerifyResult"/>.</summary>
[PublicAPI]
public static class ReCaptchaResultExtensions
{
    /// <summary>Parses <see cref="CaptchaVerifyResult.ErrorCodes"/> from the result into <see cref="ReCaptchaError"/> values.</summary>
    /// <param name="result">The verification result.</param>
    /// <returns>A read-only list of parsed error values, or an empty list when verification succeeded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> does not inherit from <see cref="CaptchaVerifyResult"/>.</exception>
    public static IReadOnlyList<ReCaptchaError> ToReCaptchaErrors(this IReCaptchaVerifyResult result)
    {
        Argument.IsNotNull(result);

        var captchaResult =
            result as CaptchaVerifyResult
            ?? throw new InvalidOperationException(
                $"{nameof(IReCaptchaVerifyResult)} implementations must also derive from {nameof(CaptchaVerifyResult)}, "
                    + $"but received '{result.GetType().FullName}'."
            );

        return captchaResult.ErrorCodes.ToReCaptchaErrors();
    }
}
