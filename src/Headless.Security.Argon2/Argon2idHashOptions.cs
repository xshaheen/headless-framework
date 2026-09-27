// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Security;

/// <summary>Argon2id cost parameters, configured through <c>UseArgon2id</c>.</summary>
/// <remarks>
/// Parallelism is fixed at 1 and the salt at 16 bytes: the libsodium implementation derives only that shape, and the
/// OWASP baseline already uses a single lane.
/// </remarks>
[PublicAPI]
public sealed class Argon2idHashOptions
{
    /// <summary>
    /// Gets or sets the memory size in KiB. Must be between <see cref="SecretHashLimits.MinArgon2idMemorySize" /> and
    /// <see cref="SecretHashLimits.MaxArgon2idMemorySize" />. Defaults to 19,456 (19 MiB, the OWASP baseline).
    /// </summary>
    public int MemorySize { get; set; } = 19_456;

    /// <summary>
    /// Gets or sets the number of passes over memory. Must be between
    /// <see cref="SecretHashLimits.MinArgon2idIterations" /> and <see cref="SecretHashLimits.MaxArgon2idIterations" />.
    /// Defaults to 2.
    /// </summary>
    public int Iterations { get; set; } = 2;

    /// <summary>Gets or sets the hash length in bytes. Must be between 16 and 64. Defaults to 32.</summary>
    public int HashSize { get; set; } = 32;
}

/// <summary>Validates <see cref="Argon2idHashOptions" />.</summary>
internal sealed class Argon2idHashOptionsValidator : AbstractValidator<Argon2idHashOptions>
{
    public Argon2idHashOptionsValidator()
    {
        RuleFor(x => x.MemorySize)
            .InclusiveBetween(SecretHashLimits.MinArgon2idMemorySize, SecretHashLimits.MaxArgon2idMemorySize);
        RuleFor(x => x.Iterations)
            .InclusiveBetween(SecretHashLimits.MinArgon2idIterations, SecretHashLimits.MaxArgon2idIterations);
        RuleFor(x => x.HashSize)
            .InclusiveBetween(SecretHashLimits.MinSaltOrHashSize, SecretHashLimits.MaxSaltOrHashSize);
    }
}
