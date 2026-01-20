// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal sealed class RabbitMQOptionsValidator : IValidateOptions<RabbitMQOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMQOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HostName))
        {
            return ValidateOptionsResult.Fail("HostName is required");
        }

        if (options.Port is not (-1 or (>= 1 and <= 65535)))
        {
            return ValidateOptionsResult.Fail("Port must be -1 (default) or between 1 and 65535");
        }

        if (string.IsNullOrWhiteSpace(options.VirtualHost))
        {
            return ValidateOptionsResult.Fail("VirtualHost is required");
        }

        if (string.IsNullOrWhiteSpace(options.ExchangeName))
        {
            return ValidateOptionsResult.Fail("ExchangeName is required");
        }

        try
        {
            RabbitMqValidation.ValidateExchangeName(options.ExchangeName);
        }
        catch (ArgumentException ex)
        {
            return ValidateOptionsResult.Fail($"Invalid ExchangeName: {ex.Message}");
        }

        return ValidateOptionsResult.Success;
    }
}
