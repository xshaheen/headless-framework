// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Aws;

/// <summary>Options for the AWS SNS SMS provider.</summary>
/// <remarks>
/// AWS SNS sends messages using the Transactional SMS type by default, which gives higher delivery
/// priority over Promotional. Credentials and region are supplied via <c>AWSOptions</c> passed to
/// <c>UseAwsSns</c> or resolved from the default AWS SDK credential chain.
/// </remarks>
public sealed class AwsSnsSmsOptions
{
    /// <summary>
    /// The alphanumeric sender ID shown to the recipient (up to 11 characters). Supported regions and
    /// character sets are documented in the AWS SNS Developer Guide.
    /// </summary>
    public required string SenderId { get; init; }

    /// <summary>
    /// Optional maximum price (in USD) per SMS that AWS will charge. When the calculated cost exceeds this
    /// value the message is not delivered. <see langword="null"/> means no upper price limit is applied.
    /// </summary>
    public decimal? MaxPrice { get; init; }
}

[UsedImplicitly]
internal sealed class AwsSnsSmsOptionsValidator : AbstractValidator<AwsSnsSmsOptions>
{
    public AwsSnsSmsOptionsValidator()
    {
        RuleFor(x => x.SenderId).NotEmpty();
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0);
    }
}
