// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Provides keyed dependency injection constants for captcha packages. Each default provider registration aliases
/// itself under the matching canonical key (<see cref="ReCaptchaV2Provider"/>, <see cref="ReCaptchaV3Provider"/>, or
/// <see cref="TurnstileProvider"/>). Callers can resolve a default provider through <see cref="ICaptchaProvider"/>
/// by canonical key or unkeyed. Keys use the <c>Headless.Captcha:</c> prefix to avoid collisions with consumer-owned
/// keyed services. Named provider instances cannot use reserved names.
/// </summary>
[PublicAPI]
public static class CaptchaConstants
{
    /// <summary>Canonical key for the default Google reCAPTCHA v2 provider.</summary>
    public const string ReCaptchaV2Provider = "Headless.Captcha:ReCaptchaV2";

    /// <summary>Canonical key for the default Google reCAPTCHA v3 provider.</summary>
    public const string ReCaptchaV3Provider = "Headless.Captcha:ReCaptchaV3";

    /// <summary>Canonical key for the default Cloudflare Turnstile provider.</summary>
    public const string TurnstileProvider = "Headless.Captcha:Turnstile";

    /// <summary>
    /// Determines whether the specified provider name uses the reserved <c>Headless.Captcha:</c> prefix.
    /// </summary>
    /// <param name="name">The candidate provider name.</param>
    /// <returns><see langword="true"/> when the name is reserved; otherwise, <see langword="false"/>.</returns>
    public static bool IsReservedProviderKey(string name)
    {
        return name.StartsWith("Headless.Captcha:", StringComparison.Ordinal);
    }
}
