// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models;
using Headless.Payments.Paymob.CashIn.Models.Callback;
using Headless.Payments.Paymob.CashIn.Models.Intentions;
using Headless.Payments.Paymob.CashIn.Models.Orders;
using Headless.Payments.Paymob.CashIn.Models.Payment;
using Headless.Payments.Paymob.CashIn.Models.Refunds;
using Headless.Payments.Paymob.CashIn.Models.Transactions;

namespace Headless.Payments.Paymob.CashIn;

/// <summary>
/// Low-level HTTP broker for the Paymob Accept (CashIn) API.
/// </summary>
/// <remarks>
/// <para>
/// Covers the full payment-collection lifecycle: order creation, payment-key issuance, channel
/// pay initiation (card iframe, wallet, kiosk, cash collection, saved token), transaction and
/// order retrieval, refunds, voids, and HMAC-based callback validation.
/// </para>
/// <para>The typical flow for legacy card/wallet/kiosk payments is:</para>
/// <list type="number">
/// <item>Authenticate via <c>IPaymobCashInAuthenticator.GetAuthenticationTokenAsync</c> (handled internally by the implementation).</item>
/// <item>Create an order with <c>CreateOrderAsync</c>.</item>
/// <item>Obtain a payment key with <c>RequestPaymentKeyAsync</c>.</item>
/// <item>Initiate the channel-specific pay call (<c>CreateWalletPayAsync</c>, <c>CreateKioskPayAsync</c>, etc.).</item>
/// <item>Paymob posts a callback to your endpoint; validate it with one of the <c>Validate</c> overloads.</item>
/// </list>
///
/// The newer Intention API (<c>CreateIntentionAsync</c>) condenses steps 2-4 into a single call
/// using a secret key rather than the API-key auth flow.
/// </remarks>
public interface IPaymobCashInBroker
{
    /// <summary>
    /// Creates a payment intention using the newer Paymob Intention API, which returns a hosted
    /// checkout URL and per-integration payment keys in a single request.
    /// </summary>
    /// <param name="request">The intention request, including amount, currency, billing data, and integration identifiers.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The intention response containing the client secret and per-integration payment keys, or
    /// <see langword="null"/> when the response body is empty.
    /// </returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInCreateIntentionResponse?> CreateIntentionAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refunds a previously captured transaction. Transaction fees apply to the refund.
    /// </summary>
    /// <param name="request">The refund request containing the transaction ID and amount in cents.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The resulting refund transaction, or <see langword="null"/> when the response body is empty.
    /// </returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInCallbackTransaction?> RefundTransactionAsync(
        CashInRefundRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Voids a transaction that occurred on the same business day. No transaction fees apply.
    /// </summary>
    /// <param name="request">The void request containing the transaction ID.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The resulting void transaction, or <see langword="null"/> when the response body is empty.
    /// </returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInCallbackTransaction?> VoidTransactionAsync(
        CashInVoidRefundRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates an order, which is the logical container for one or more transactions in Paymob Accept.
    /// </summary>
    /// <param name="request">
    /// Order details including amount in cents, currency, and optional shipping and delivery information.
    /// Use <c>CashInCreateOrderRequest.CreateOrder</c> for standard orders or
    /// <c>CashInCreateOrderRequest.CreateDeliveryOrder</c> when Accept's delivery service is required.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created order including its server-assigned numeric ID.</returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInCreateOrderResponse> CreateOrderAsync(
        CashInCreateOrderRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Obtains a short-lived payment key that authorises a single payment attempt for a specific
    /// integration and amount. Pass this key to the channel-specific pay methods or to the hosted iframe.
    /// </summary>
    /// <param name="request">
    /// Payment key request including the order ID, integration ID, billing data, amount in cents,
    /// and expiration period in seconds.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The response containing the payment key string.</returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInPaymentKeyResponse> RequestPaymentKeyAsync(
        CashInPaymentKeyRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Initiates a mobile-wallet payment and returns a redirect URL for the customer to complete OTP verification.
    /// </summary>
    /// <param name="paymentKey">The payment key obtained from <c>RequestPaymentKeyAsync</c>.</param>
    /// <param name="phoneNumber">The customer's wallet-registered phone number.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The wallet pay response, which includes a <c>RedirectUrl</c> the customer must follow to
    /// enter their OTP and a flag indicating whether initiation succeeded.
    /// </returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInWalletPayResponse> CreateWalletPayAsync(
        string paymentKey,
        string phoneNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Initiates a kiosk (Aman/Accept aggregator) payment and returns a bill reference number
    /// the customer uses to pay at the kiosk.
    /// </summary>
    /// <param name="paymentKey">The payment key obtained from <c>RequestPaymentKeyAsync</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The kiosk pay response containing the bill reference in <c>Data.BillReference</c>.</returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInKioskPayResponse> CreateKioskPayAsync(string paymentKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a cash-collection payment, dispatching a courier to collect cash from the customer.
    /// </summary>
    /// <param name="paymentKey">The payment key obtained from <c>RequestPaymentKeyAsync</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The cash-collection pay response.</returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInCashCollectionPayResponse> CreateCashCollectionPayAsync(
        string paymentKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Charges a previously tokenised card without requiring the customer to re-enter card details.
    /// </summary>
    /// <param name="paymentKey">The payment key obtained from <c>RequestPaymentKeyAsync</c>.</param>
    /// <param name="savedToken">The saved card token issued by Paymob during the original card payment.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The saved-token pay response. Check <c>ErrorOccured</c> and <c>IsSuccess</c> to determine the
    /// outcome; a 3-D Secure redirect URL is present in <c>RedirectionUrl</c> when <c>Is3dSecure</c> is true.
    /// </returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInSavedTokenPayResponse> CreateSavedTokenPayAsync(
        string paymentKey,
        string savedToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retrieves a paginated list of transactions.
    /// </summary>
    /// <param name="request">Optional pagination and filter parameters. Pass <see langword="null"/> for the first page with default settings.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A page of transactions including next/previous cursor URLs, or <see langword="null"/> when the
    /// response body is empty.
    /// </returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInTransactionsPage?> GetTransactionsPageAsync(
        CashInTransactionsPageRequest? request = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retrieves a single transaction by its Paymob-assigned numeric ID.
    /// </summary>
    /// <param name="transactionId">The Paymob transaction ID.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The transaction, or <see langword="null"/> when not found or the response body is empty.</returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInTransaction?> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single order by its Paymob-assigned numeric ID.
    /// </summary>
    /// <param name="orderId">The Paymob order ID.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The order, or <see langword="null"/> when not found or the response body is empty.</returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInOrder?> GetOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a paginated list of orders.
    /// </summary>
    /// <param name="request">Optional pagination parameters. Pass <see langword="null"/> for the first page with default settings.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A page of orders, or <see langword="null"/> when the response body is empty.
    /// </returns>
    /// <exception cref="PaymobCashInException">The HTTP request to Paymob failed.</exception>
    Task<CashInOrdersPage?> GetOrdersPageAsync(
        CashInOrdersPageRequest? request = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Verifies a Paymob Accept callback by comparing an HMAC-SHA512 digest of the concatenated
    /// string against the HMAC value received from Paymob.
    /// </summary>
    /// <param name="concatenatedString">
    /// The pre-built concatenation of transaction or token fields in Paymob's defined order.
    /// Use <c>CashInCallbackTransaction.ToConcatenatedString</c> or <c>CashInCallbackToken.ToConcatenatedString</c>
    /// to produce this string.
    /// </param>
    /// <param name="hmac">The HMAC value received from Paymob in the callback request.</param>
    /// <returns><see langword="true"/> if the digest matches; <see langword="false"/> otherwise.</returns>
    [Pure]
    bool Validate(string concatenatedString, string hmac);

    /// <summary>
    /// Verifies a Paymob Accept transaction callback received via query parameters (GET callback).
    /// </summary>
    /// <param name="queryParameters">The deserialized query parameters from the Paymob callback request.</param>
    /// <returns><see langword="true"/> if the HMAC digest matches; <see langword="false"/> otherwise.</returns>
    [Pure]
    bool Validate(CashInCallbackQueryParameters queryParameters);

    /// <summary>
    /// Verifies a Paymob Accept transaction callback received via the POST body.
    /// </summary>
    /// <param name="transaction">The transaction object from the callback body.</param>
    /// <param name="hmac">The HMAC value received from Paymob in the callback request.</param>
    /// <returns><see langword="true"/> if the HMAC digest matches; <see langword="false"/> otherwise.</returns>
    [Pure]
    bool Validate(CashInCallbackTransaction transaction, string hmac);

    /// <summary>
    /// Verifies a Paymob Accept saved-card token callback received via the POST body.
    /// </summary>
    /// <param name="token">The token object from the callback body.</param>
    /// <param name="hmac">The HMAC value received from Paymob in the callback request.</param>
    /// <returns><see langword="true"/> if the HMAC digest matches; <see langword="false"/> otherwise.</returns>
    [Pure]
    bool Validate(CashInCallbackToken token, string hmac);

    /// <summary>
    /// Builds the embed URL for the Paymob Accept hosted iframe.
    /// </summary>
    /// <param name="iframeId">The iframe integration ID configured in the Paymob dashboard.</param>
    /// <param name="token">The payment key obtained from <c>RequestPaymentKeyAsync</c>.</param>
    /// <returns>The full iframe embed URL to render in the browser.</returns>
    [Pure]
    string CreateIframeSrc(string iframeId, string token);
}
