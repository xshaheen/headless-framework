// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Idempotency;

internal sealed class IdempotencyLookupBodyValidator : AbstractValidator<IdempotencyLookupBody>
{
    // The messages name the field, never its value: a validation problem is served and may be logged. The text rules
    // are the store's own, so a key or tenant the store would refuse answers a field-named 400 instead of reaching it.
    public IdempotencyLookupBodyValidator()
    {
        RuleFor(static body => body.Key)
            .NotEmpty()
            .WithErrorCode(DashboardValidation.RequiredRule)
            .WithMessage("A lookup needs an idempotency key.")
            .MaximumLength(IdempotencyFieldLimits.KeyMaxLength)
            .WithErrorCode(DashboardValidation.MaxLengthRule)
            .WithMessage($"key takes at most {IdempotencyFieldLimits.KeyMaxLength} characters.")
            .Must(_IsPortable)
            .WithErrorCode(DashboardValidation.FormatRule)
            .WithMessage(
                "key must not start or end with white space, contain a NUL character, or contain an unpaired surrogate."
            );

        RuleFor(static body => body.TenantId)
            .NotEmpty()
            .WithErrorCode(DashboardValidation.RequiredRule)
            .WithMessage("tenantId must name a tenant; leave it out to read the host scope.")
            .MaximumLength(IdempotencyFieldLimits.TenantIdMaxLength)
            .WithErrorCode(DashboardValidation.MaxLengthRule)
            .WithMessage($"tenantId takes at most {IdempotencyFieldLimits.TenantIdMaxLength} characters.")
            .Must(_IsPortable)
            .WithErrorCode(DashboardValidation.FormatRule)
            .WithMessage(
                "tenantId must not start or end with white space, contain a NUL character, or contain an unpaired surrogate."
            )
            .When(static body => body.TenantId is not null);
    }

    // The store's own portability check, so the module and the store never disagree about which text is a key.
    private static bool _IsPortable(string? value)
    {
        try
        {
            Argument.IsPortableKey(value);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
