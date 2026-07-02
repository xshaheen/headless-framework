// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;

namespace Tests;

/// <summary>Builders for SMS request values used across SMS provider tests.</summary>
public static class SmsRequests
{
    /// <summary>A single-recipient <see cref="SendSingleSmsRequest"/>.</summary>
    public static SendSingleSmsRequest Single(
        string text = "Hello world",
        int code = 20,
        string number = "1001234567",
        string? messageId = null
    )
    {
        return new SendSingleSmsRequest
        {
            Destination = new SmsRequestDestination(code, number),
            Text = text,
            MessageId = messageId,
        };
    }

    /// <summary>A multi-recipient <see cref="SendBulkSmsRequest"/> for <see cref="IBulkSmsSender"/>.</summary>
    public static SendBulkSmsRequest Bulk(string text = "Hello world", params (int Code, string Number)[] destinations)
    {
        return new SendBulkSmsRequest
        {
            Destinations = destinations.Select(d => new SmsRequestDestination(d.Code, d.Number)).ToList(),
            Text = text,
        };
    }
}
