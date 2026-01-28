// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Aws;

public sealed class AwsSnsSmsOptions
{
    public required string SenderId { get; init; }

    public required decimal? MaxPrice { get; init; }
}

[UsedImplicitly]
internal sealed class AwsSnsSmsOptionsValidator : AbstractValidator<AwsSnsSmsOptions>
{
    public AwsSnsSmsOptionsValidator()
    {
        RuleFor(x => x.SenderId).NotEmpty();
        RuleFor(x => x.MaxPrice).GreaterThan(0);
    }
}
