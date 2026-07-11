// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// Represents a CashOut disbursement transaction returned by the Paymob CashOut API.
/// </summary>
/// <remarks>
/// Use the status helpers (<c>IsSuccess</c>, <c>IsPending</c>, <c>IsFailed</c>, etc.) to
/// interpret the outcome without string-matching <c>DisbursementStatus</c> and <c>StatusCode</c>
/// directly. For Aman kiosk disbursements, <c>AmanCashingDetails</c> carries the billing reference
/// the recipient uses to collect cash.
/// </remarks>
[PublicAPI]
public sealed record CashOutTransaction
{
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; init; }

    [JsonPropertyName("issuer")]
    public string? Issuer { get; init; }

    [JsonPropertyName("msisdn")]
    public string? Msisdn { get; init; }

    /// <summary>The disbursed amount in Egyptian Pounds (EGP), mirroring the value sent in the disburse request.</summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

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

    // set (not init): [JsonExtensionData] cannot bind through init-only metadata and fails deserialization
    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }

    // Msisdn, FullName, AmanCashingDetails, StatusDescription, and ExtensionData can carry recipient PII or
    // arbitrary provider fields; they are redacted so a failure log that renders this transaction (see
    // PaymobCashOutLoggerExtensions) cannot leak them.
    public override string ToString()
    {
        return $"CashOutTransaction {{ TransactionId = {TransactionId}, Issuer = {Issuer}, Amount = {Amount}, DisbursementStatus = {DisbursementStatus}, StatusCode = {StatusCode}, CreatedAt = {CreatedAt}, UpdatedAt = {UpdatedAt}, Msisdn = [redacted], FullName = [redacted], AmanCashingDetails = [redacted] }}";
    }

    /// <summary>Returns <see langword="true"/> when the disbursement completed successfully.</summary>
    public bool IsSuccess()
    {
        return DisbursementStatus is "success" or "successful";
    }

    /// <summary>Returns <see langword="true"/> when the disbursement failed.</summary>
    public bool IsFailed()
    {
        return DisbursementStatus is "failed";
    }

    /// <summary>
    /// Returns <see langword="true"/> when the disbursement is pending processing by the provider
    /// (status code <c>8000</c>). This is an intermediate state; poll or await a callback to
    /// confirm the final outcome.
    /// </summary>
    public bool IsPending()
    {
        return DisbursementStatus is "pending" && StatusCode is "8000";
    }

    /// <summary>Returns <see langword="true"/> when the failure is due to an authentication error (HTTP 401 from the provider).</summary>
    public bool IsAuthenticationError()
    {
        return IsFailed() && StatusCode is "401";
    }

    /// <summary>
    /// Returns <see langword="true"/> when the Vodafone wallet recipient does not have sufficient
    /// Vodafone Cash balance to receive the transfer (Vodafone status code 618).
    /// </summary>
    public bool IsNotHaveVodafoneCashError()
    {
        return IsFailed() && Issuer is "vodafone" && StatusCode is "618";
    }

    /// <summary>
    /// Returns <see langword="true"/> when the Etisalat wallet recipient cannot receive the transfer
    /// (Etisalat status code 90040).
    /// </summary>
    public bool IsNotHaveEtisalatCashError()
    {
        return IsFailed() && Issuer is "etisalat" && StatusCode is "90040";
    }

    /// <summary>
    /// Returns <see langword="true"/> when the disbursement failed because the Vodafone or Etisalat
    /// provider system is temporarily unavailable.
    /// </summary>
    public bool IsProviderDownError()
    {
        return IsFailed()
            && (
                (Issuer is "vodafone" && StatusCode is "501" or "6097")
                || (Issuer is "etisalat" && StatusCode is "90005" or "90006")
            );
    }

    /// <summary>
    /// Returns <see langword="true"/> when Paymob rejected the request due to invalid field values
    /// (status code 400). Inspect <c>StatusDescription</c> for field-level detail.
    /// </summary>
    public bool IsRequestValidationError()
    {
        return IsFailed() && StatusCode is "400";
    }
}
