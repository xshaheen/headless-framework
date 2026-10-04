// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.MultiTenancy;

internal sealed class TenantTelemetryOptionsValidator : AbstractValidator<TenantTelemetryOptions>
{
    public TenantTelemetryOptionsValidator()
    {
        RuleFor(x => x.LogAttributeName).NotEmpty().When(x => x.EnrichLogs);
        // Messaging inbox metrics read AttributeName even when trace enrichment is off.
        RuleFor(x => x.AttributeName).NotEmpty();
    }
}
