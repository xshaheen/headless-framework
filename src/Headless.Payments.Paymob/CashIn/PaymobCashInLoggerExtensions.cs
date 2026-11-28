// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.Logging;

namespace Headless.Payments.Paymob.CashIn;

// Logs the outcome and the provider HTTP status, never the request or the response body, which carry customer
// data.
internal static partial class PaymobCashInLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "WalletCashInNotStarted",
        Level = LogLevel.Error,
        Message = "Wallet cash-in for order {OrderId} was {Outcome}. Provider status {StatusCode}"
    )]
    internal static partial void LogWalletCashInNotStarted(
        this ILogger logger,
        Exception? exception,
        PaymobCashInOutcome outcome,
        HttpStatusCode? statusCode,
        long orderId
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "KioskCashInNotStarted",
        Level = LogLevel.Error,
        Message = "Kiosk cash-in for order {OrderId} was {Outcome}. Provider status {StatusCode}"
    )]
    internal static partial void LogKioskCashInNotStarted(
        this ILogger logger,
        Exception? exception,
        PaymobCashInOutcome outcome,
        HttpStatusCode? statusCode,
        long orderId
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "SavedTokenCashInNotStarted",
        Level = LogLevel.Error,
        Message = "Saved token cash-in for order {OrderId} was {Outcome}. Provider status {StatusCode}"
    )]
    internal static partial void LogSavedTokenCashInNotStarted(
        this ILogger logger,
        Exception? exception,
        PaymobCashInOutcome outcome,
        HttpStatusCode? statusCode,
        long orderId
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "EmptyPaymentKeyReceived",
        Level = LogLevel.Error,
        Message = "Received empty payment key from Paymob"
    )]
    internal static partial void LogEmptyPaymentKeyReceived(this ILogger logger);

    [LoggerMessage(
        EventId = 5,
        EventName = "CannotCreatePaymentKey",
        Level = LogLevel.Error,
        Message = "Cannot create payment key for order {OrderId}, integration {IntegrationId}, amount {AmountCents}: {Outcome}, provider status {StatusCode}"
    )]
    internal static partial void LogCannotCreatePaymentKey(
        this ILogger logger,
        Exception? exception,
        PaymobCashInOutcome outcome,
        HttpStatusCode? statusCode,
        long orderId,
        long integrationId,
        long amountCents
    );

    [LoggerMessage(
        EventId = 6,
        EventName = "CannotCreateOrder",
        Level = LogLevel.Error,
        Message = "Cannot create order, amount cents {AmountCents}: {Outcome}, provider status {StatusCode}"
    )]
    internal static partial void LogCannotCreateOrder(
        this ILogger logger,
        Exception? exception,
        PaymobCashInOutcome outcome,
        HttpStatusCode? statusCode,
        long amountCents
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "UnusableCashInPayResponse",
        Level = LogLevel.Critical,
        Message = "Paymob accepted the payment for order {OrderId} but returned no way to finish it; the outcome is unknown"
    )]
    internal static partial void LogUnusableCashInPayResponse(this ILogger logger, long orderId);
}
