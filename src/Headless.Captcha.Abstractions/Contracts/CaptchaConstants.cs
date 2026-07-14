// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Keyed-DI constants for the captcha packages. Each provider's default (unkeyed) registration also aliases itself
/// under the matching canonical key below (<c>UseReCaptchaV2</c> → <see cref="ReCaptchaV2Provider"/>,
/// <c>UseReCaptchaV3</c> → <see cref="ReCaptchaV3Provider"/>, <c>UseTurnstile</c> → <see cref="TurnstileProvider"/>),
/// so a default provider is resolvable through <see cref="ICaptchaProvider"/> by its canonical key as well as
/// unkeyed. The keys are namespaced under <c>Headless.Captcha:</c> so they cannot collide with consumer-owned keyed
/// services. Named provider instances must not use a reserved name — the setup builder rejects them.
/// </summary>
[PublicAPI]
public static class CaptchaConstants
{
    /// <summary>Canonical keyed-DI key for the default Google reCAPTCHA v2 provider.</summary>
    public const string ReCaptchaV2Provider = "Headless.Captcha:ReCaptchaV2";

    /// <summary>Canonical keyed-DI key for the default Google reCAPTCHA v3 provider.</summary>
    public const string ReCaptchaV3Provider = "Headless.Captcha:ReCaptchaV3";

    /// <summary>Canonical keyed-DI key for the default Cloudflare Turnstile provider.</summary>
    public const string TurnstileProvider = "Headless.Captcha:Turnstile";

    /// <summary>
    /// Indicates whether <paramref name="name"/> is reserved for the framework's provider keys — any name under the
    /// <c>Headless.Captcha:</c> namespace, which the framework owns.
    /// </summary>
    /// <param name="name">The candidate provider name.</param>
    /// <returns><see langword="true"/> when the name is reserved.</returns>
    public static bool IsReservedProviderKey(string name)
    {
        return name.StartsWith("Headless.Captcha:", StringComparison.Ordinal);
    }
}
