// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Sms;

[PublicAPI]
public sealed class SendSingleSmsRequest
{
    public string? MessageId { get; init; }

    public required SmsRequestDestination Destination { get; init; }

    public required string Text { get; init; }

    public IDictionary<string, object>? Properties { get; init; }
}

[PublicAPI]
public sealed record SmsRequestDestination(int Code, string Number)
{
    public override string ToString() => $"+{Code}{Number}";
}
