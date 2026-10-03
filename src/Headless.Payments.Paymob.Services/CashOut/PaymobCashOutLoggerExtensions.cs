// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Internal;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Payments.Paymob.Services.CashOut.Requests;
using Headless.Payments.Paymob.Services.CashOut.Responses;
using Headless.Payments.Paymob.Services.Resources;
using Headless.Primitives;
using Microsoft.Extensions.Logging;

namespace Headless.Payments.Paymob.Services.CashOut;

internal static partial class PaymobCashOutLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "UnexpectedAcceptCashOutResponse",
        Level = LogLevel.Critical,
        Message = "Unexpected response to Accept CashOut {Response}"
    )]
    public static partial void LogUnexpectedAcceptCashOutResponse(this ILogger logger, CashOutTransaction? response);

    // Logs the provider HTTP status (non-PII) instead of the raw response body, which may carry recipient
    // PII / provider internal detail. The exception still carries the full message and stack.
    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToStartCashOut",
        Level = LogLevel.Critical,
        Message = "Failed to start cash out. Provider responded with status {StatusCode}"
    )]
    public static partial void LogFailedToStartCashOut(
        this ILogger logger,
        Exception exception,
        HttpStatusCode statusCode
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "CashOutBudgetExceeded",
        Level = LogLevel.Critical,
        Message = "Cash out budget exceeded {Response}"
    )]
    public static partial void LogCashOutBudgetExceeded(this ILogger logger, CashOutTransaction response);
}
