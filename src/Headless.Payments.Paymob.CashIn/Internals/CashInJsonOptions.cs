// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;

namespace Headless.Payments.Paymob.CashIn.Internals;

internal static class CashInJsonOptions
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = PaymobCashInJsonSerializerContext.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };
}
