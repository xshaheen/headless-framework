// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashOut.Models;

namespace Framework.Payments.Paymob.CashOut.Internals;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(CashOutDisburseRequest))]
[JsonSerializable(typeof(CashOutTransaction))]
[JsonSerializable(typeof(CashOutGetTransactionsRequest))]
[JsonSerializable(typeof(CashOutAuthenticationResponse))]
internal sealed partial class PaymobCashOutJsonSerializerContext : JsonSerializerContext;
