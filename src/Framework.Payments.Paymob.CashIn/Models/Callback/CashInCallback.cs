// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallback
{
    /// <summary>See: <see cref="CashInCallbackTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// <para>
    /// If <see cref="Type"/> = <see cref="CashInCallbackTypes.Transaction"/> this will be <see cref="CashInCallbackTransaction"/>
    /// </para>
    /// <para>
    /// If <see cref="Type"/> = <see cref="CashInCallbackTypes.Token"/> this will be <see cref="CashInCallbackToken"/>
    /// </para>
    /// </summary>
    [JsonPropertyName("obj")]
    public object? Obj { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
