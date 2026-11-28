// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Payments.Paymob.Resources;
using Headless.Primitives;
using Microsoft.Extensions.Logging;

namespace Headless.Payments.Paymob.CashIn;

internal sealed class PaymobCashInService(IPaymobCashInBroker broker, ILogger<PaymobCashInService> logger)
    : IPaymobCashInService
{
    public async Task<CashInResult<PaymobCardCashInResponse>> StartAsync(
        PaymobCardCashInRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var started = await _StartAsync(
                request.Customer,
                request.Amount,
                request.CardIntegrationId,
                request.ExpirationSeconds,
                request.MerchantOrderId,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!started.IsSucceeded)
        {
            return _Forward<PaymobCardCashInResponse>(started);
        }

        var (orderId, paymentKey) = started.Data;

        return CashInResult.Success(
            new PaymobCardCashInResponse(
                IframeSrc: broker.CreateIframeSrc(iframeId: request.IframeSrc, token: paymentKey),
                PaymentKey: paymentKey,
                OrderId: orderId.ToString(CultureInfo.InvariantCulture),
                Expiration: request.ExpirationSeconds
            )
        );
    }

    public async Task<CashInResult<PaymobWalletCashInResponse>> StartAsync(
        PaymobWalletCashInRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var started = await _StartAsync(
                request.Customer,
                request.Amount,
                request.WalletIntegrationId,
                request.ExpirationSeconds,
                request.MerchantOrderId,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!started.IsSucceeded)
        {
            return _Forward<PaymobWalletCashInResponse>(started);
        }

        var (orderId, paymentKey) = started.Data;

        var pay = await broker
            .CreateWalletPayAsync(paymentKey, request.WalletPhoneNumber, cancellationToken)
            .ConfigureAwait(false);

        if (!pay.IsSucceeded)
        {
            logger.LogWalletCashInNotStarted(pay.Error.Exception, pay.Outcome, pay.Error.StatusCode, orderId);

            return _FromPayment<PaymobWalletCashInResponse>(pay.Outcome, pay.Value is not null, orderId);
        }

        if (string.IsNullOrWhiteSpace(pay.Value.RedirectUrl))
        {
            logger.LogUnusableCashInPayResponse(orderId);

            // Paymob accepted the payment, so it exists in some state even though the customer has no page to
            // finish it on. The caller must look the order up, not start another payment for it.
            return CashInResult.Unknown<PaymobWalletCashInResponse>(
                PaymobMessageDescriptor.CashIn.OutcomeUnknown(),
                orderId.ToString(CultureInfo.InvariantCulture)
            );
        }

        return CashInResult.Success(
            new PaymobWalletCashInResponse(
                RedirectUrl: pay.Value.RedirectUrl,
                OrderId: orderId.ToString(CultureInfo.InvariantCulture),
                Expiration: request.ExpirationSeconds
            )
        );
    }

    public async Task<CashInResult<PaymobKioskCashInResponse>> StartAsync(
        PaymobKioskCashInRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var started = await _StartAsync(
                request.Customer,
                request.Amount,
                request.KioskIntegrationId,
                request.ExpirationSeconds,
                request.MerchantOrderId,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!started.IsSucceeded)
        {
            return _Forward<PaymobKioskCashInResponse>(started);
        }

        var (orderId, paymentKey) = started.Data;

        var pay = await broker.CreateKioskPayAsync(paymentKey, cancellationToken).ConfigureAwait(false);

        if (!pay.IsSucceeded)
        {
            logger.LogKioskCashInNotStarted(pay.Error.Exception, pay.Outcome, pay.Error.StatusCode, orderId);

            return _FromPayment<PaymobKioskCashInResponse>(pay.Outcome, pay.Value is not null, orderId);
        }

        if (pay.Value.Data is null)
        {
            logger.LogUnusableCashInPayResponse(orderId);

            // Paymob accepted the payment and may have issued a bill the customer can pay, so it is not safe to
            // issue another one for the same order.
            return CashInResult.Unknown<PaymobKioskCashInResponse>(
                PaymobMessageDescriptor.CashIn.OutcomeUnknown(),
                orderId.ToString(CultureInfo.InvariantCulture)
            );
        }

        return CashInResult.Success(
            new PaymobKioskCashInResponse(
                BillingReference: pay.Value.Data.BillReference.ToString(CultureInfo.InvariantCulture),
                OrderId: orderId.ToString(CultureInfo.InvariantCulture),
                Expiration: request.ExpirationSeconds
            )
        );
    }

    public async Task<CashInResult<PaymobCardSavedTokenCashInResponse>> StartAsync(
        PaymobCardSavedTokenCashInRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var started = await _StartAsync(
                request.Customer,
                request.Amount,
                request.SavedTokenIntegrationId,
                request.ExpirationSeconds,
                request.MerchantOrderId,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!started.IsSucceeded)
        {
            return _Forward<PaymobCardSavedTokenCashInResponse>(started);
        }

        var (orderId, paymentKey) = started.Data;

        var pay = await broker
            .CreateSavedTokenPayAsync(paymentKey, request.CardToken, cancellationToken)
            .ConfigureAwait(false);

        if (!pay.IsSucceeded)
        {
            logger.LogSavedTokenCashInNotStarted(pay.Error.Exception, pay.Outcome, pay.Error.StatusCode, orderId);

            return _FromPayment<PaymobCardSavedTokenCashInResponse>(pay.Outcome, pay.Value is not null, orderId);
        }

        return CashInResult.Success(
            new PaymobCardSavedTokenCashInResponse
            {
                OrderId = orderId.ToString(CultureInfo.InvariantCulture),
                TransactionId = pay.Value.Id,
                RedirectionUrl = pay.Value.RedirectionUrl,
                Is3DSecure = pay.Value.Is3DSecure.Equals("true", StringComparison.OrdinalIgnoreCase),
                IsSuccess = pay.Value.IsSuccess(),
            }
        );
    }

    public async Task<CashInResult<CashInCreateIntentionResponse>> StartAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var result = await broker.CreateIntentionAsync(request, cancellationToken).ConfigureAwait(false);

        // An intention moves no money until the customer pays on the checkout page, so a lost answer leaves at most
        // an unused intention behind.
        return result.IsSucceeded
            ? CashInResult.Success(result.Value)
            : CashInResult.Rejected<CashInCreateIntentionResponse>(
                PaymobMessageDescriptor.CashIn.ProviderConnectionFailed()
            );
    }

    public async Task<CashInResult<CashInCallbackTransaction>> VoidAsync(
        PaymobVoidRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var result = await broker
            .VoidTransactionAsync(new(request.TransactionId), cancellationToken)
            .ConfigureAwait(false);

        return _FromMoneyMovement(result);
    }

    public async Task<CashInResult<CashInCallbackTransaction>> RefundAsync(
        PaymobRefundRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        // A zero or negative refund was sent to Paymob as is; refuse it here.
        Argument.IsPositive(request.Amount);

        // Refunds round a fraction of a cent down, so partial refunds of one charge never add up to more than it:
        // three refunds of 100/3 ask for 9999 cents, not 10002. Charges round up, so a full refund of a sub-cent
        // charge returns one cent less than was charged.
        var amountCents = (long)Math.Floor(request.Amount * 100);

        // A positive amount under one cent rounds to zero, which Paymob would receive as an empty refund.
        Argument.IsPositive(amountCents, "A refund must be at least one cent.", paramName: nameof(request.Amount));

        var result = await broker
            .RefundTransactionAsync(
                new(request.TransactionId, amountCents.ToString(CultureInfo.InvariantCulture)),
                cancellationToken
            )
            .ConfigureAwait(false);

        return _FromMoneyMovement(result);
    }

    #region Helpers

    // Creates the order and the payment key. Neither moves money, so any failure here is a rejection: the caller
    // may start again, with a new merchant order ID when the order may already exist.
    private async Task<CashInResult<(long OrderId, string PaymentKey)>> _StartAsync(
        PaymobCashInCustomerData customer,
        decimal amount,
        long integrationId,
        int expiration,
        string? merchantOrderId,
        CancellationToken cancellationToken
    )
    {
        // A zero or negative charge was sent to Paymob as is; refuse it here.
        Argument.IsPositive(amount);

        var amountCents = (long)Math.Ceiling(amount * 100);
        var orderRequest = CashInCreateOrderRequest.CreateOrder(amountCents, merchantOrderId: merchantOrderId);
        var order = await broker.CreateOrderAsync(orderRequest, cancellationToken).ConfigureAwait(false);

        if (!order.IsSucceeded)
        {
            logger.LogCannotCreateOrder(order.Error.Exception, order.Outcome, order.Error.StatusCode, amountCents);

            return CashInResult.Rejected<(long, string)>(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        var billingData = new CashInBillingData(
            customer.FirstName,
            customer.LastName,
            customer.PhoneNumber,
            customer.Email
        );

        var keyRequest = new CashInPaymentKeyRequest(
            integrationId: integrationId,
            orderId: order.Value.Id,
            billingData,
            amountCents,
            lockOrderWhenPaid: true,
            expiration: expiration
        );

        var key = await broker.RequestPaymentKeyAsync(keyRequest, cancellationToken).ConfigureAwait(false);

        if (!key.IsSucceeded)
        {
            logger.LogCannotCreatePaymentKey(
                key.Error.Exception,
                key.Outcome,
                key.Error.StatusCode,
                keyRequest.OrderId,
                keyRequest.IntegrationId,
                keyRequest.AmountCents
            );

            return CashInResult.Rejected<(long, string)>(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        if (string.IsNullOrWhiteSpace(key.Value.PaymentKey))
        {
            logger.LogEmptyPaymentKeyReceived();

            return CashInResult.Rejected<(long, string)>(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return CashInResult.Success((order.Value.Id, key.Value.PaymentKey));
    }

    private static CashInResult<CashInCallbackTransaction> _FromMoneyMovement(
        PaymobCashInResult<CashInCallbackTransaction> result
    )
    {
        return result.IsSucceeded
            ? CashInResult.Success(result.Value)
            : _FromPayment<CashInCallbackTransaction>(result.Outcome, result.Value is not null, orderId: null);
    }

    // A rejection with a parsed body is Paymob declining the operation; one without a body is a 4xx or a request
    // that was never sent.
    private static CashInResult<T> _FromPayment<T>(PaymobCashInOutcome outcome, bool declined, long? orderId)
    {
        var order = orderId?.ToString(CultureInfo.InvariantCulture);

        if (outcome is PaymobCashInOutcome.Unknown)
        {
            return CashInResult.Unknown<T>(PaymobMessageDescriptor.CashIn.OutcomeUnknown(), order);
        }

        return CashInResult.Rejected<T>(
            declined
                ? PaymobMessageDescriptor.CashIn.Declined()
                : PaymobMessageDescriptor.CashIn.ProviderConnectionFailed(),
            order
        );
    }

    private static CashInResult<T> _Forward<T>(CashInResult<(long OrderId, string PaymentKey)> result)
    {
        return result.IsRejected ? CashInResult.Rejected<T>(result.Error) : CashInResult.Unknown<T>(result.Error!);
    }

    #endregion
}

/// <summary>
/// High-level service that orchestrates Paymob Accept (CashIn) payment collection across multiple
/// channels: card iframe, saved card token, mobile wallet, and kiosk.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>StartAsync</c> overload executes the full legacy Paymob flow internally — order
/// creation, payment-key issuance, and channel-specific pay initiation — and returns a
/// channel-specific response ready for the client. Every method returns a <c>CashInResult</c> and never
/// throws for a Paymob failure.
/// </para>
/// <para>
/// Branch on <c>CashInResult.Outcome</c>. <c>Rejected</c> means no money moved, and <c>Error</c> says why
/// (<c>cash_in:declined</c> when Paymob refused the charge, refund, or void). <c>Unknown</c>
/// (<c>cash_in:outcome_unknown</c>) means the money may have moved: resolve it with the transaction callback or
/// an inquiry before you charge, refund, or void again: <c>IPaymobCashInBroker.GetOrderAsync</c> with
/// <c>CashInResult.OrderId</c> for a charge, or <c>GetTransactionAsync</c> with the transaction ID for a refund or void.
/// </para>
/// <para>
/// The Intention API flow (<c>StartAsync(CashInCreateIntentionRequest)</c>) bypasses the
/// multi-step legacy flow and delegates directly to the broker. An intention moves no money, so it is never
/// unknown.
/// </para>
/// <para>
/// Register via <c>SetupPaymobServices.AddPaymobServices</c>. The implementation depends on
/// <c>IPaymobCashInBroker</c> and is itself scoped.
/// </para>
/// </remarks>
[PublicAPI]
public interface IPaymobCashInService
{
    /// <summary>
    /// Initiates a card payment and returns the hosted iframe URL and payment key.
    /// </summary>
    /// <param name="request">Card payment parameters including amount, customer data, and integration ID.</param>
    /// <param name="cancellationToken">Token to cancel the multi-step operation.</param>
    /// <returns>
    /// A succeeded result with the iframe embed URL (<c>IframeSrc</c>), raw payment key, order ID, and expiration
    /// in seconds, or a rejected result. This call moves no money, so it is never unknown.
    /// </returns>
    Task<CashInResult<PaymobCardCashInResponse>> StartAsync(
        PaymobCardCashInRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Charges a previously tokenised card and returns the transaction outcome.
    /// </summary>
    /// <param name="request">Saved-token payment parameters including the card token and integration ID.</param>
    /// <param name="cancellationToken">Token to cancel the multi-step operation.</param>
    /// <returns>
    /// A succeeded result when the card was charged or awaits 3-D Secure (with a redirect URL), with the
    /// transaction and order IDs; a rejected result when no money moved; or an unknown result when the charge may
    /// have gone through. Resolve an unknown charge before you charge the card again.
    /// </returns>
    Task<CashInResult<PaymobCardSavedTokenCashInResponse>> StartAsync(
        PaymobCardSavedTokenCashInRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Initiates a mobile-wallet payment and returns the OTP redirect URL.
    /// </summary>
    /// <param name="request">Wallet payment parameters including the wallet phone number and integration ID.</param>
    /// <param name="cancellationToken">Token to cancel the multi-step operation.</param>
    /// <returns>
    /// A succeeded result with the redirect URL the customer must follow and the order ID, a rejected result, or an
    /// unknown result when a payment may exist for the order.
    /// </returns>
    Task<CashInResult<PaymobWalletCashInResponse>> StartAsync(
        PaymobWalletCashInRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Initiates a kiosk payment and returns the bill reference the customer uses to pay at the outlet.
    /// </summary>
    /// <param name="request">Kiosk payment parameters including the integration ID.</param>
    /// <param name="cancellationToken">Token to cancel the multi-step operation.</param>
    /// <returns>
    /// A succeeded result with the billing reference number and the order ID, a rejected result, or an unknown
    /// result when a bill may exist for the order.
    /// </returns>
    Task<CashInResult<PaymobKioskCashInResponse>> StartAsync(
        PaymobKioskCashInRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a payment intention using the newer Paymob Intention API.
    /// </summary>
    /// <param name="request">The intention request including amount, currency, billing data, and integration identifiers.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A succeeded result with the intention response from Paymob, or a rejected result.</returns>
    Task<CashInResult<CashInCreateIntentionResponse>> StartAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refunds a previously captured transaction. A refund is a reverse transaction; fees apply.
    /// </summary>
    /// <param name="request">The refund request containing the transaction ID and amount to refund.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A succeeded result with the refund transaction, a rejected result when no money moved, or an unknown result
    /// when the refund may have gone through. Resolve an unknown refund before you send it again.
    /// </returns>
    Task<CashInResult<CashInCallbackTransaction>> RefundAsync(
        PaymobRefundRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Voids a transaction that occurred on the same business day. No fees apply.
    /// </summary>
    /// <param name="request">The void request containing the transaction ID to cancel.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A succeeded result with the void transaction, a rejected result, or an unknown result when the void may have
    /// gone through. Resolve an unknown void before you send it again.
    /// </returns>
    Task<CashInResult<CashInCallbackTransaction>> VoidAsync(
        PaymobVoidRequest request,
        CancellationToken cancellationToken = default
    );
}
