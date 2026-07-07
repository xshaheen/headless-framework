// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashOut.Models;

namespace Headless.Payments.Paymob.CashOut.Internals;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(CashOutDisburseRequest))]
[JsonSerializable(typeof(CashOutTransaction))]
[JsonSerializable(typeof(CashOutGetTransactionsRequest))]
[JsonSerializable(typeof(CashOutGetTransactionsResponse))]
[JsonSerializable(typeof(CashOutBudgetResponse))]
[JsonSerializable(typeof(CashOutAuthenticationResponse))]
// object/JsonElement metadata is required for [JsonExtensionData] values and the
// polymorphic status_description field; without it unknown response fields throw.
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class PaymobCashOutJsonSerializerContext : JsonSerializerContext;
