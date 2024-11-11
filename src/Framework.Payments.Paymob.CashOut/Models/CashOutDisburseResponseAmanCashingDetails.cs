using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashOut.Models;

[PublicAPI]
public sealed record CashOutDisburseResponseAmanCashingDetails
{
    [JsonPropertyName("bill_reference")]
    public int? BillingReference { get; init; }

    [JsonPropertyName("is_paid")]
    public bool IsPaid { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
