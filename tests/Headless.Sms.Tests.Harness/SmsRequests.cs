// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Testing;

/// <summary>Builders for <see cref="SendSingleSmsRequest"/> values used across SMS provider tests.</summary>
public static class SmsRequests
{
    /// <summary>A single-destination request.</summary>
    public static SendSingleSmsRequest Single(
        string text = "Hello world",
        int code = 20,
        string number = "1001234567",
        string? messageId = null
    )
    {
        return new SendSingleSmsRequest
        {
            Destinations = [new SmsRequestDestination(code, number)],
            Text = text,
            MessageId = messageId,
        };
    }

    /// <summary>A multi-destination (batch) request.</summary>
    public static SendSingleSmsRequest Batch(
        string text = "Hello world",
        params (int Code, string Number)[] destinations
    )
    {
        return new SendSingleSmsRequest
        {
            Destinations = destinations.Select(d => new SmsRequestDestination(d.Code, d.Number)).ToList(),
            Text = text,
        };
    }
}
