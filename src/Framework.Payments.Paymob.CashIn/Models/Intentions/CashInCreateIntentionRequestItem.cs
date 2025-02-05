namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionRequestItem
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
