// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Sms;

[PublicAPI]
public sealed class SendSingleSmsRequest
{
    public string? MessageId { get; init; }

    public required IReadOnlyList<SmsRequestDestination> Destinations { get; init; }

    public required string Text { get; init; }

    public IDictionary<string, object>? Properties { get; init; }

    public bool IsBatch => Destinations.Count > 1;
}

[PublicAPI]
public sealed record SmsRequestDestination(int Code, string Number)
{
    public override string ToString() => ToString(hasPlusPrefix: false);

    public string ToString(bool hasPlusPrefix)
    {
        var format = hasPlusPrefix ? $"+{Code}{Number}" : (FormattableString)$"{Code}{Number}";

        return format.ToString(CultureInfo.InvariantCulture);
    }
}
