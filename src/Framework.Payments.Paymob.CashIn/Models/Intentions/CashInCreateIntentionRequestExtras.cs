namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionRequestExtras
{
    [JsonPropertyName("ee")]
    public required int Ee { get; init; }
}