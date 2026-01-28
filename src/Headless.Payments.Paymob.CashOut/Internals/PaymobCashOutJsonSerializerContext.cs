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
[JsonSerializable(typeof(CashOutAuthenticationResponse))]
internal sealed partial class PaymobCashOutJsonSerializerContext : JsonSerializerContext;
