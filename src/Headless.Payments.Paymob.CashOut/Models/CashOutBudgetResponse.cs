// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// The response returned by the Paymob CashOut budget inquiry endpoint.
/// </summary>
/// <remarks>
/// Paymob reports the available budget as a human-readable sentence in <see cref="CurrentBudget"/>
/// (for example, <c>"Your current budget is 888.25 LE"</c>) rather than as a numeric field. The
/// endpoint is rate-limited to 5 requests per minute; when throttled it responds with HTTP 429,
/// which the broker surfaces as <c>PaymobCashOutException</c> instead of this model.
/// </remarks>
[PublicAPI]
public sealed class CashOutBudgetResponse
{
    /// <summary>
    /// A human-readable description of the account's available budget, for example
    /// <c>"Your current budget is 888.25 LE"</c>. May be <see langword="null"/> when Paymob omits it.
    /// </summary>
    [JsonPropertyName("current_budget")]
    public string? CurrentBudget { get; init; }

    /// <summary>Any additional fields returned by Paymob that are not mapped to a typed property.</summary>
    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
