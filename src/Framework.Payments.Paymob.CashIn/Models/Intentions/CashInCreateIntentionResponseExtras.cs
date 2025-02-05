// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponseExtras
{
    [JsonPropertyName("creation_extras")]
    public required CashInCreateIntentionResponseCreationExtras CreationExtras { get; init; }

    [JsonPropertyName("confirmation_extras")]
    public required object? ConfirmationExtras { get; init; }
}
