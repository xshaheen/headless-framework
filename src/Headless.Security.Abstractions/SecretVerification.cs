// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>The result of <see cref="ISecretHasher.Verify" />.</summary>
/// <param name="Succeeded"><see langword="true" /> when the secret matches the stored hash.</param>
/// <param name="Rehashed">
/// A fresh hash of the secret under the configured algorithm and cost, present only after a success whose stored
/// algorithm or parameters are below the configured ones. Persist it in place of the stored hash.
/// </param>
[PublicAPI]
public readonly record struct SecretVerification(bool Succeeded, string? Rehashed)
{
    /// <summary>A failed verification.</summary>
    public static SecretVerification Failed => default;
}
