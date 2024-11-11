// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Callback;
using Framework.Payments.Paymob.CashIn.Models.Orders;
using Framework.Payments.Paymob.CashIn.Models.Payment;
using Framework.Payments.Paymob.CashIn.Models.Transactions;

namespace Framework.Payments.Paymob.CashIn;

public interface IPaymobCashInBroker
{
    /// <summary>Create order. Order is a logical container for a transaction(s).</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInCreateOrderResponse> CreateOrderAsync(CashInCreateOrderRequest request);

    /// <summary>
    /// Get a payment key which is used to authenticate payment request and verifying transaction
    /// request metadata.
    /// </summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInPaymentKeyResponse> RequestPaymentKeyAsync(CashInPaymentKeyRequest request);

    /// <summary>Create wallet pay</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInWalletPayResponse> CreateWalletPayAsync(string paymentKey, string phoneNumber);

    /// <summary>Create kiosk pay</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInKioskPayResponse> CreateKioskPayAsync(string paymentKey);

    /// <summary>Create Cash collection pay.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInCashCollectionPayResponse> CreateCashCollectionPayAsync(string paymentKey);

    /// <summary>Create saved token pay</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInSavedTokenPayResponse> CreateSavedTokenPayAsync(string paymentKey, string savedToken);

    /// <summary>Get transaction page.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInTransactionsPage?> GetTransactionsPageAsync(CashInTransactionsPageRequest? request = null);

    /// <summary>Get transaction by id.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInTransaction?> GetTransactionAsync(string transactionId);

    /// <summary>Get order by id.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInOrder?> GetOrderAsync(string orderId);

    /// <summary>Get order page.</summary>
    /// <exception cref="PaymobCashInException"></exception>
    [Pure]
    Task<CashInOrdersPage?> GetOrdersPageAsync(CashInOrdersPageRequest? request = null);

    /// <summary>Validate the identity and integrity for "Paymob Accept" callback submission.</summary>
    /// <param name="concatenatedString">Object concatenated string.</param>
    /// <param name="hmac">Received HMAC.</param>
    /// <returns>True if is valid, otherwise return false.</returns>
    [Pure]
    bool Validate(string concatenatedString, string hmac);

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
