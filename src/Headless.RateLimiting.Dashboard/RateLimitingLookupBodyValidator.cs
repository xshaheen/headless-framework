// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.RateLimiting;

internal sealed class RateLimitingLookupBodyValidator : AbstractValidator<RateLimitingLookupBody>
{
    /// <summary>The longest subject a lookup or a reset accepts.</summary>
    public const int MaxSubjectLength = 512;

    /// <summary>The longest purpose a lookup or a reset accepts.</summary>
    public const int MaxPurposeLength = 200;

    // The messages name the field, never its value: a validation problem is served and may be logged.
    public RateLimitingLookupBodyValidator()
    {
        RuleFor(static body => body.Purpose)
            .NotEmpty()
            .WithErrorCode(DashboardValidation.RequiredRule)
            .WithMessage("A lookup needs a purpose.")
            .MaximumLength(MaxPurposeLength)
            .WithErrorCode(DashboardValidation.MaxLengthRule)
            .WithMessage($"purpose takes at most {MaxPurposeLength} characters.");

        RuleFor(static body => body.Subject)
            .NotEmpty()
            .WithErrorCode(DashboardValidation.RequiredRule)
            .WithMessage("A lookup needs a subject.")
            .MaximumLength(MaxSubjectLength)
            .WithErrorCode(DashboardValidation.MaxLengthRule)
            .WithMessage($"subject takes at most {MaxSubjectLength} characters.");
    }
}
