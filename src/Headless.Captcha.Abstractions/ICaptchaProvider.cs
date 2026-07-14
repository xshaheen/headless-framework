// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Resolves <see cref="ICaptchaVerifier"/> instances registered under a name — either named instances added through
/// the setup builder (for example <c>setup.AddNamed("turnstile", i =&gt; i.UseTurnstile(…))</c>) or a default provider's canonical key
/// (the <c>Headless.Captcha:</c> constants on <see cref="CaptchaConstants"/>). Inject the concrete verifier
/// interface (<c>IReCaptchaV3Verifier</c>, <c>ITurnstileVerifier</c>) directly when you need provider-only data.
/// </summary>
[PublicAPI]
public interface ICaptchaProvider
{
    /// <summary>
    /// Gets the names under which verifiers are registered — every named instance plus a default provider's canonical
    /// key (the <c>Headless.Captcha:</c> constants on <see cref="CaptchaConstants"/>). Use this to validate an
    /// externally supplied provider name (header, config, database row) before resolving it, rather than probing
    /// <see cref="GetVerifier"/> and catching, or reconstructing the set from <see cref="CaptchaConstants"/> (which
    /// omits consumer-chosen names).
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }

    /// <summary>Gets the verifier registered under <paramref name="name"/>.</summary>
    /// <param name="name">The provider name (or a default provider's canonical key).</param>
    /// <returns>The resolved verifier.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no verifier is registered under <paramref name="name"/>.</exception>
    ICaptchaVerifier GetVerifier(string name);

    /// <summary>Gets the verifier registered under <paramref name="name"/>, or <see langword="null"/> when none is registered.</summary>
    /// <param name="name">The provider name (or a default provider's canonical key).</param>
    /// <returns>The resolved verifier, or <see langword="null"/>.</returns>
    ICaptchaVerifier? GetVerifierOrNull(string name);
}
