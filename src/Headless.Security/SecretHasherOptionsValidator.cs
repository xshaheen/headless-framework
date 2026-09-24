// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Security;

/// <summary>Validates <see cref="SecretHasherOptions" />.</summary>
/// <remarks>
/// Lives here rather than beside the options class because the abstractions package carries no FluentValidation
/// reference. It deliberately takes no dependency on the registered algorithms: validators run while options are
/// being built, and algorithms read options, so that dependency would re-enter options construction. The
/// "configured algorithm is registered" check runs at host start instead.
/// </remarks>
internal sealed class SecretHasherOptionsValidator : AbstractValidator<SecretHasherOptions>
{
    public SecretHasherOptionsValidator()
    {
        RuleFor(x => x.Algorithm).NotEmpty();
        RuleFor(x => x.MaxSecretLength).InclusiveBetween(1, SecretHashLimits.MaxSecretLength);

        RuleFor(x => x.Argon2id).NotNull();
        RuleFor(x => x.Argon2id.MemorySize)
            .InclusiveBetween(SecretHashLimits.MinArgon2idMemorySize, SecretHashLimits.MaxArgon2idMemorySize)
            .When(x => x.Argon2id is not null);
        RuleFor(x => x.Argon2id.Iterations)
            .InclusiveBetween(SecretHashLimits.MinArgon2idIterations, SecretHashLimits.MaxArgon2idIterations)
            .When(x => x.Argon2id is not null);
        RuleFor(x => x.Argon2id.HashSize)
            .InclusiveBetween(SecretHashLimits.MinSaltOrHashSize, SecretHashLimits.MaxSaltOrHashSize)
            .When(x => x.Argon2id is not null);

        RuleFor(x => x.Pbkdf2Sha256).NotNull();
        RuleFor(x => x.Pbkdf2Sha256.Iterations)
            .InclusiveBetween(SecretHashLimits.MinPbkdf2Iterations, SecretHashLimits.MaxPbkdf2Iterations)
            .When(x => x.Pbkdf2Sha256 is not null);
        RuleFor(x => x.Pbkdf2Sha256.SaltSize)
            .InclusiveBetween(SecretHashLimits.MinSaltOrHashSize, SecretHashLimits.MaxSaltOrHashSize)
            .When(x => x.Pbkdf2Sha256 is not null);
        RuleFor(x => x.Pbkdf2Sha256.HashSize)
            .InclusiveBetween(SecretHashLimits.MinSaltOrHashSize, SecretHashLimits.MaxSaltOrHashSize)
            .When(x => x.Pbkdf2Sha256 is not null);

        RuleFor(x => x.CostCheck).NotNull();
        RuleFor(x => x.CostCheck.Mode).IsInEnum().When(x => x.CostCheck is not null);
        RuleFor(x => x.CostCheck.MinimumDuration)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .When(x => x.CostCheck is not null);
        RuleFor(x => x.CostCheck.MaximumDuration)
            .GreaterThan(x => x.CostCheck.MinimumDuration)
            .When(x => x.CostCheck is not null);
    }
}
