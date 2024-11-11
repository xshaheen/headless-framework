using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashOut.Models;

[PublicAPI]
public sealed record CashOutTransaction
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; init; }

    [JsonPropertyName("issuer")]
    public string? Issuer { get; init; }

    [JsonPropertyName("msisdn")]
    public string? Msisdn { get; init; }

    [JsonPropertyName("amount")]
    public double Amount { get; init; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }

    [JsonPropertyName("disbursement_status")]
    public required string DisbursementStatus { get; init; }

    [JsonPropertyName("status_code")]
    public required string StatusCode { get; init; }

    /// <summary>It can has multi form string, object </summary>
    [JsonPropertyName("status_description")]
    public object? StatusDescription { get; init; }

    [JsonPropertyName("aman_cashing_details")]
    public CashOutDisburseResponseAmanCashingDetails? AmanCashingDetails { get; init; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }

    public bool IsSuccess()
    {
        return DisbursementStatus is "success" or "successful";
    }

    public bool IsFailed()
    {
        return DisbursementStatus is "failed";
    }

    public bool IsPending()
    {
        return DisbursementStatus is "pending" && StatusCode is "8000";
    }

    public bool IsAuthenticationError()
    {
        return IsFailed() && StatusCode is "401";
    }

    public bool IsNotHaveVodafoneCashError()
    {
        return IsFailed() && Issuer is "vodafone" && StatusCode is "618";
    }

    public bool IsNotHaveEtisalatCashError()
    {
        return IsFailed() && Issuer is "etisalat" && StatusCode is "90040";
    }

    public bool IsProviderDownError()
    {
        return IsFailed()
            && (
                (Issuer is "vodafone" && StatusCode is "501" or "6097")
                || (Issuer is "etisalat" && StatusCode is "90005" or "90006")
            );
    }

    public bool IsRequestValidationError()
    {
        return IsFailed() && StatusCode is "400";
    }
}
