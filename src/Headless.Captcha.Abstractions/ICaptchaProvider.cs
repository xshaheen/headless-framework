// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Resolves named or default <see cref="ICaptchaVerifier"/> instances registered by the application.
/// </summary>
[PublicAPI]
public interface ICaptchaProvider
{
    /// <summary>
    /// Gets registered verifier names, including named instances and canonical default provider keys.
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }

    /// <summary>Gets the verifier registered under <paramref name="name"/>.</summary>
    /// <param name="name">The provider name or canonical key.</param>
    /// <returns>The resolved verifier.</returns>
    /// <exception cref="InvalidOperationException">No verifier is registered under <paramref name="name"/>.</exception>
    ICaptchaVerifier GetVerifier(string name);

    /// <summary>Gets the verifier registered under <paramref name="name"/>, or <see langword="null"/> when not found.</summary>
    /// <param name="name">The provider name or canonical key.</param>
    /// <returns>The resolved verifier, or <see langword="null"/> when not found.</returns>
    ICaptchaVerifier? GetVerifierOrNull(string name);
}
