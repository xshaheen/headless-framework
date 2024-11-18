// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Sms.Aws;

public sealed class AwsSnsSmsOptions
{
    public required string SenderId { get; init; }
}

[UsedImplicitly]
internal sealed class AwsSnsSmsOptionsValidator : AbstractValidator<AwsSnsSmsOptions>
{
    public AwsSnsSmsOptionsValidator()
    {
        RuleFor(x => x.SenderId).NotEmpty();
    }
}
