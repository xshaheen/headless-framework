// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.AuditLog;

/// <summary>Validates <see cref="AuditLogOptions"/>.</summary>
internal sealed class AuditLogOptionsValidator : AbstractValidator<AuditLogOptions>
{
    public AuditLogOptionsValidator()
    {
        RuleFor(x => x.DefaultExcludedProperties).NotNull();

        RuleFor(x => x.SensitiveValueTransformer)
            .NotNull()
            .When(x => x.SensitiveDataStrategy == SensitiveDataStrategy.Transform)
            .WithMessage("SensitiveValueTransformer must be configured when SensitiveDataStrategy is Transform.");
    }
}
