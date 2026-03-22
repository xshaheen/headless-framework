// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.RabbitMq;

internal sealed class RabbitMqOptionsValidator : AbstractValidator<RabbitMqOptions>
{
    public RabbitMqOptionsValidator()
    {
        RuleFor(x => x.HostName).NotEmpty().WithMessage("HostName is required");

        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("UserName is required and must be configured explicitly");
        RuleFor(x => x.UserName)
            .Must(u => !u.Equals("guest", StringComparison.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.UserName))
            .WithMessage("UserName cannot be 'guest' - use a secure username for production environments");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required and must be configured explicitly");
        RuleFor(x => x.Password)
            .Must(p => !p.Equals("guest", StringComparison.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Password))
            .WithMessage("Password cannot be 'guest' - use a secure password for production environments");

        RuleFor(x => x.Port)
            .Must(p => p is -1 or (>= 1 and <= 65535))
            .WithMessage("Port must be -1 (default) or between 1 and 65535");

        RuleFor(x => x.VirtualHost).NotEmpty().WithMessage("VirtualHost is required");
        RuleFor(x => x.ExchangeName).NotEmpty().WithMessage("ExchangeName is required");

        RuleFor(x => x.ExchangeName)
            .Must(name =>
            {
                try
                {
                    RabbitMqValidation.ValidateExchangeName(name);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            })
            .When(x => !string.IsNullOrWhiteSpace(x.ExchangeName))
            .WithMessage("Invalid ExchangeName format");
    }
}
