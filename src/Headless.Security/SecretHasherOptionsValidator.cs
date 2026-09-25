// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Security;

/// <summary>Validates <see cref="SecretHasherOptions" />.</summary>
/// <remarks>
/// Lives here rather than beside the options class because the abstractions package carries no FluentValidation
/// reference. Algorithm cost parameters are validated by each algorithm's own options validator.
/// </remarks>
internal sealed class SecretHasherOptionsValidator : AbstractValidator<SecretHasherOptions>
{
    public SecretHasherOptionsValidator()
    {
        RuleFor(x => x.MaxSecretLength).InclusiveBetween(1, SecretHashLimits.MaxSecretLength);

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
