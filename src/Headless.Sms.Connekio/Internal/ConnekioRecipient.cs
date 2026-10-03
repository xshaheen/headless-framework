// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Connekio.Internal;

internal sealed class ConnekioRecipient
{
    [JsonPropertyName("msisdn")]
    public required string Msisdn { get; init; }
}
