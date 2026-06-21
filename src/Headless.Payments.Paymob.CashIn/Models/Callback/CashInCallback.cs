// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

/// <summary>
/// Represents the top-level envelope of a Paymob Accept webhook callback body.
/// </summary>
/// <remarks>
/// Paymob posts two callback shapes to the same endpoint, distinguished by <c>Type</c>.
/// Deserialize <c>Obj</c> to <c>CashInCallbackTransaction</c> when <c>Type</c> is
/// <c>CashInCallbackTypes.Transaction</c>, or to <c>CashInCallbackToken</c> when it is
/// <c>CashInCallbackTypes.Token</c>. Verify authenticity with
/// <c>IPaymobCashInBroker.Validate</c> before processing.
/// </remarks>
[PublicAPI]
public sealed class CashInCallback
{
    /// <summary>
    /// The callback type discriminator. See <c>CashInCallbackTypes</c> for known values
    /// (<c>TRANSACTION</c> or <c>TOKEN</c>).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// The callback payload. Cast or re-deserialize to the appropriate concrete type based on
    /// <c>Type</c>:
    /// <list type="bullet">
    /// <item><c>CashInCallbackTypes.Transaction</c> — deserialize as <c>CashInCallbackTransaction</c>.</item>
    /// <item><c>CashInCallbackTypes.Token</c> — deserialize as <c>CashInCallbackToken</c>.</item>
    /// </list>
    /// </summary>
    [JsonPropertyName("obj")]
    public object? Obj { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
