// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Security;

/// <summary>PBKDF2-SHA256 cost parameters, configured through <c>UsePbkdf2Sha256</c>.</summary>
[PublicAPI]
public sealed class Pbkdf2Sha256HashOptions
{
    /// <summary>
    /// Gets or sets the iteration count. Must be between <see cref="SecretHashLimits.MinPbkdf2Iterations" /> and
    /// <see cref="SecretHashLimits.MaxPbkdf2Iterations" />. Defaults to 600,000 (OWASP).
    /// </summary>
    public int Iterations { get; set; } = 600_000;

    /// <summary>Gets or sets the salt length in bytes. Must be between 16 and 64. Defaults to 16.</summary>
    public int SaltSize { get; set; } = 16;

    /// <summary>Gets or sets the hash length in bytes. Must be between 16 and 64. Defaults to 32.</summary>
    public int HashSize { get; set; } = 32;
}

/// <summary>Validates <see cref="Pbkdf2Sha256HashOptions" />.</summary>
internal sealed class Pbkdf2Sha256HashOptionsValidator : AbstractValidator<Pbkdf2Sha256HashOptions>
{
    public Pbkdf2Sha256HashOptionsValidator()
    {
        RuleFor(x => x.Iterations)
            .InclusiveBetween(SecretHashLimits.MinPbkdf2Iterations, SecretHashLimits.MaxPbkdf2Iterations);
        RuleFor(x => x.SaltSize)
            .InclusiveBetween(SecretHashLimits.MinSaltOrHashSize, SecretHashLimits.MaxSaltOrHashSize);
        RuleFor(x => x.HashSize)
            .InclusiveBetween(SecretHashLimits.MinSaltOrHashSize, SecretHashLimits.MaxSaltOrHashSize);
    }
}
