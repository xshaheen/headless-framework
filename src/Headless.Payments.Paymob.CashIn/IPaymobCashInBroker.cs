// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models;
using Headless.Payments.Paymob.CashIn.Models.Callback;
using Headless.Payments.Paymob.CashIn.Models.Intentions;
using Headless.Payments.Paymob.CashIn.Models.Orders;
using Headless.Payments.Paymob.CashIn.Models.Payment;
using Headless.Payments.Paymob.CashIn.Models.Refunds;
using Headless.Payments.Paymob.CashIn.Models.Transactions;

namespace Headless.Payments.Paymob.CashIn;

public interface IPaymobCashInBroker
{
    /// <summary>Create intention request.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInCreateIntentionResponse?> CreateIntentionAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Refund a transaction.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInCallbackTransaction?> RefundTransactionAsync(
        CashInRefundRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Void a transaction.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInCallbackTransaction?> VoidTransactionAsync(
        CashInVoidRefundRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Create order. Order is a logical container for a transaction(s).</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInCreateOrderResponse> CreateOrderAsync(
        CashInCreateOrderRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get a payment key which is used to authenticate payment request and verifying transaction
    /// request metadata.
    /// </summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInPaymentKeyResponse> RequestPaymentKeyAsync(
        CashInPaymentKeyRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Create wallet pay</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInWalletPayResponse> CreateWalletPayAsync(
        string paymentKey,
        string phoneNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>Create kiosk pay</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInKioskPayResponse> CreateKioskPayAsync(string paymentKey, CancellationToken cancellationToken = default);

    /// <summary>Create Cash collection pay.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInCashCollectionPayResponse> CreateCashCollectionPayAsync(
        string paymentKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Create saved token pay</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInSavedTokenPayResponse> CreateSavedTokenPayAsync(
        string paymentKey,
        string savedToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get transaction page.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInTransactionsPage?> GetTransactionsPageAsync(
        CashInTransactionsPageRequest? request = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get transaction by id.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInTransaction?> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>Get order by id.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInOrder?> GetOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>Get order page.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    Task<CashInOrdersPage?> GetOrdersPageAsync(
        CashInOrdersPageRequest? request = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Validate the identity and integrity for "Paymob Accept" callback submission.</summary>
    /// <param name="concatenatedString">Object concatenated string.</param>
    /// <param name="hmac">Received HMAC.</param>
    /// <returns>True if is valid, otherwise return false.</returns>
    [Pure]
    bool Validate(string concatenatedString, string hmac);

    /// <summary>Validate the identity and integrity for "Paymob Accept" callback submission.</summary>
    /// <param name="queryParameters">Received query parameters.</param>
    /// <returns>True if is valid, otherwise return false.</returns>
    [Pure]
    bool Validate(CashInCallbackQueryParameters queryParameters);

    /// <summary>Validate the identity and integrity for "Paymob Accept" callback submission.</summary>
    /// <param name="transaction">Received transaction.</param>
    /// <param name="hmac">Received HMAC.</param>
    /// <returns>True if is valid, otherwise return false.</returns>
    [Pure]
    bool Validate(CashInCallbackTransaction transaction, string hmac);

    /// <summary>Validate the identity and integrity for "Paymob Accept" callback submission.</summary>
    /// <param name="token">Received token.</param>
    /// <param name="hmac">Received HMAC.</param>
    /// <returns>True if is valid, otherwise return false.</returns>
    [Pure]
    bool Validate(CashInCallbackToken token, string hmac);

    /// <summary>Create iframe src url.</summary>
    /// <param name="iframeId">Iframe Id.</param>
    /// <param name="token">Payment token.</param>
    [Pure]
    string CreateIframeSrc(string iframeId, string token);
}
