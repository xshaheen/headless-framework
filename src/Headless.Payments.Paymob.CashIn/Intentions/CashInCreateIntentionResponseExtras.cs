// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn;

public sealed class CashInCreateIntentionResponseExtras
{
    [JsonPropertyName("creation_extras")]
    public required CashInCreateIntentionResponseCreationExtras CreationExtras { get; init; }

    [JsonPropertyName("confirmation_extras")]
    public required object? ConfirmationExtras { get; init; }
}
