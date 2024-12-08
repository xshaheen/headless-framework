// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Vodafone;

public sealed class VodafoneSmsOptions
{
    public required string SendSmsEndpoint { get; init; } = "https://e3len.vodafone.com.eg/web2sms/sms/submit/";

    public required string Sender { get; init; }

    public required string AccountId { get; init; }

    public required string Password { get; init; }

    public required string SecureHash { get; init; }
}

internal sealed class VodafoneSmsOptionsValidator : AbstractValidator<VodafoneSmsOptions>
{
    public VodafoneSmsOptionsValidator()
    {
        RuleFor(x => x.SendSmsEndpoint).NotEmpty().HttpUrl();
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.SecureHash).NotEmpty();
    }
}
