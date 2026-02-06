// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Payment;
using Headless.Payments.Paymob.Services.CashIn.Requests;
using Microsoft.Extensions.Logging;

namespace Headless.Payments.Paymob.Services.CashIn;

internal static partial class PaymobCashInLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToCreateWalletCashIn",
        Level = LogLevel.Error,
        Message = "Failed to create wallet cash-in: {Request}"
    )]
    public static partial void LogFailedToCreateWalletCashIn(
        this ILogger logger,
        Exception exception,
        PaymobWalletCashInRequest request
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToCreateKioskCashIn",
        Level = LogLevel.Error,
        Message = "Failed to create kiosk cash-in: {Request}"
    )]
    public static partial void LogFailedToCreateKioskCashIn(
        this ILogger logger,
        Exception exception,
        PaymobKioskCashInRequest request
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "FailedToCreateSavedTokenCashIn",
        Level = LogLevel.Error,
        Message = "Failed to create saved token cash-in: {Request}"
    )]
    public static partial void LogFailedToCreateSavedTokenCashIn(
        this ILogger logger,
        Exception exception,
        PaymobCardSavedTokenCashInRequest request
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "EmptyPaymentKeyReceived",
        Level = LogLevel.Error,
        Message = "Received empty payment key from Paymob"
    )]
    public static partial void LogEmptyPaymentKeyReceived(this ILogger logger);

    [LoggerMessage(
        EventId = 5,
        EventName = "CannotCreatePaymentKey",
        Level = LogLevel.Error,
        Message = "Cannot create payment key for order {OrderId}, integration {IntegrationId}, amount {AmountCents}"
    )]
    public static partial void LogCannotCreatePaymentKey(
        this ILogger logger,
        Exception exception,
        int orderId,
        int integrationId,
        int amountCents
    );

    [LoggerMessage(
        EventId = 6,
        EventName = "CannotCreateOrder",
        Level = LogLevel.Error,
        Message = "Cannot create order, amount cents: {AmountCents}"
    )]
    public static partial void LogCannotCreateOrder(this ILogger logger, Exception exception, int amountCents);
}
