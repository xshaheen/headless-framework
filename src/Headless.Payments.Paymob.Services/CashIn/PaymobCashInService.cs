// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Exceptions;
using Headless.Payments.Paymob.CashIn;
using Headless.Payments.Paymob.CashIn.Models.Callback;
using Headless.Payments.Paymob.CashIn.Models.Intentions;
using Headless.Payments.Paymob.CashIn.Models.Orders;
using Headless.Payments.Paymob.CashIn.Models.Payment;
using Headless.Payments.Paymob.Services.CashIn.Requests;
using Headless.Payments.Paymob.Services.CashIn.Responses;
using Headless.Payments.Paymob.Services.Resources;
using Microsoft.Extensions.Logging;

namespace Headless.Payments.Paymob.Services.CashIn;

/// <summary>
/// High-level service that orchestrates Paymob Accept (CashIn) payment collection across multiple
/// channels: card iframe, saved card token, mobile wallet, and kiosk.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>StartAsync</c> overload executes the full legacy Paymob flow internally — order
/// creation, payment-key issuance, and channel-specific pay initiation — and returns a
/// channel-specific response ready for the client. Provider connectivity failures are surfaced as
/// <c>ConflictException</c> with a structured error descriptor from <c>PaymobMessageDescriptor</c>.
/// </para>
/// <para>
/// The Intention API flow (<c>StartAsync(CashInCreateIntentionRequest)</c>) bypasses the
/// multi-step legacy flow and delegates directly to the broker.
/// </para>
/// <para>
/// Register via the Services package setup class. The implementation depends on
/// <c>IPaymobCashInBroker</c> and is itself scoped.
/// </para>
/// </remarks>
public interface IPaymobCashInService
{
    /// <summary>
    /// Initiates a card payment and returns the hosted iframe URL and payment key.
    /// </summary>
    /// <param name="request">Card payment parameters including amount, customer data, and integration ID.</param>
    /// <param name="cancellationToken">Token to cancel the multi-step operation.</param>
    /// <returns>
    /// A response containing the iframe embed URL (<c>IframeSrc</c>), raw payment key, order ID,
    /// and expiration in seconds.
    /// </returns>
    Task<PaymobCardCashInResponse> StartAsync(
        PaymobCardCashInRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Charges a previously tokenised card and returns the transaction outcome.
    /// </summary>
    /// <param name="request">Saved-token payment parameters including the card token and integration ID.</param>
    /// <param name="cancellationToken">Token to cancel the multi-step operation.</param>
    /// <returns>
    /// A response indicating whether the charge succeeded, whether 3-D Secure is required
    /// (with a redirect URL), and the resulting transaction and order IDs.
    /// </returns>
    Task<PaymobCardSavedTokenCashInResponse> StartAsync(
        PaymobCardSavedTokenCashInRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Initiates a mobile-wallet payment and returns the OTP redirect URL.
    /// </summary>
    /// <param name="request">Wallet payment parameters including the wallet phone number and integration ID.</param>
    /// <param name="cancellationToken">Token to cancel the multi-step operation.</param>
    /// <returns>A response containing the redirect URL the customer must follow and the order ID.</returns>
    Task<PaymobWalletCashInResponse> StartAsync(
        PaymobWalletCashInRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Initiates a kiosk payment and returns the bill reference the customer uses to pay at the outlet.
    /// </summary>
    /// <param name="request">Kiosk payment parameters including the integration ID.</param>
    /// <param name="cancellationToken">Token to cancel the multi-step operation.</param>
    /// <returns>A response containing the billing reference number and the order ID.</returns>
    Task<PaymobKioskCashInResponse> StartAsync(
        PaymobKioskCashInRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a payment intention using the newer Paymob Intention API.
    /// </summary>
    /// <param name="request">The intention request including amount, currency, billing data, and integration identifiers.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The intention response from Paymob, or <see langword="null"/> when the response body is empty.
    /// </returns>
    Task<CashInCreateIntentionResponse?> StartAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refunds a previously captured transaction. A refund is a reverse transaction; fees apply.
    /// </summary>
    /// <param name="request">The refund request containing the transaction ID and amount to refund.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The resulting refund transaction from Paymob, or <see langword="null"/> when the response body is empty.
    /// </returns>
    Task<CashInCallbackTransaction?> RefundAsync(
        PaymobRefundRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Voids a transaction that occurred on the same business day. No fees apply.
    /// </summary>
    /// <param name="request">The void request containing the transaction ID to cancel.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The resulting void transaction from Paymob, or <see langword="null"/> when the response body is empty.
    /// </returns>
    Task<CashInCallbackTransaction?> VoidAsync(
        PaymobVoidRequest request,
        CancellationToken cancellationToken = default
    );
}

public sealed class PaymobCashInService(IPaymobCashInBroker broker, ILogger<PaymobCashInService> logger)
    : IPaymobCashInService
{
    public async Task<PaymobCardCashInResponse> StartAsync(
        PaymobCardCashInRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var (orderId, paymentKey) = await _StartAsync(
                request.Customer,
                request.Amount,
                request.CardIntegrationId,
                request.ExpirationSeconds,
                request.MerchantOrderId,
                cancellationToken
            )
            .ConfigureAwait(false);

        return new PaymobCardCashInResponse(
            IframeSrc: broker.CreateIframeSrc(iframeId: request.IframeSrc, token: paymentKey),
            PaymentKey: paymentKey,
            OrderId: orderId.ToString(CultureInfo.InvariantCulture),
            Expiration: request.ExpirationSeconds
        );
    }

    public async Task<PaymobWalletCashInResponse> StartAsync(
        PaymobWalletCashInRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var (orderId, paymentKey) = await _StartAsync(
                request.Customer,
                request.Amount,
                request.WalletIntegrationId,
                request.ExpirationSeconds,
                request.MerchantOrderId,
                cancellationToken
            )
            .ConfigureAwait(false);

        CashInWalletPayResponse payResponse;

        try
        {
            payResponse = await broker
                .CreateWalletPayAsync(paymentKey, request.WalletPhoneNumber, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogFailedToCreateWalletCashIn(e, request);

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        if (string.IsNullOrWhiteSpace(payResponse.RedirectUrl) || !payResponse.IsCreatedSuccessfully())
        {
            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return new PaymobWalletCashInResponse(
            RedirectUrl: payResponse.RedirectUrl,
            OrderId: orderId.ToString(CultureInfo.InvariantCulture),
            Expiration: request.ExpirationSeconds
        );
    }

    public async Task<PaymobKioskCashInResponse> StartAsync(
        PaymobKioskCashInRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var (orderId, paymentKey) = await _StartAsync(
                request.Customer,
                request.Amount,
                request.KioskIntegrationId,
                request.ExpirationSeconds,
                request.MerchantOrderId,
                cancellationToken
            )
            .ConfigureAwait(false);

        CashInKioskPayResponse payResponse;

        try
        {
            payResponse = await broker.CreateKioskPayAsync(paymentKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogFailedToCreateKioskCashIn(e, request);

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        if (payResponse.Data is null || !payResponse.IsCreatedSuccessfully())
        {
            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return new PaymobKioskCashInResponse(
            BillingReference: payResponse.Data.BillReference.ToString(CultureInfo.InvariantCulture),
            OrderId: orderId.ToString(CultureInfo.InvariantCulture),
            Expiration: request.ExpirationSeconds
        );
    }

    public async Task<PaymobCardSavedTokenCashInResponse> StartAsync(
        PaymobCardSavedTokenCashInRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var (orderId, paymentKey) = await _StartAsync(
                request.Customer,
                request.Amount,
                request.SavedTokenIntegrationId,
                request.ExpirationSeconds,
                request.MerchantOrderId,
                cancellationToken
            )
            .ConfigureAwait(false);

        CashInSavedTokenPayResponse payResponse;

        try
        {
            payResponse = await broker
                .CreateSavedTokenPayAsync(paymentKey, request.CardToken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogFailedToCreateSavedTokenCashIn(e, request);

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        if (payResponse.ErrorOccured.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return new PaymobCardSavedTokenCashInResponse
        {
            OrderId = orderId.ToString(CultureInfo.InvariantCulture),
            TransactionId = payResponse.Id,
            RedirectionUrl = payResponse.RedirectionUrl,
            Is3DSecure = payResponse.Is3DSecure.Equals("true", StringComparison.OrdinalIgnoreCase),
            IsSuccess = payResponse.IsSuccess(),
        };
    }

    public Task<CashInCreateIntentionResponse?> StartAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        return broker.CreateIntentionAsync(request, cancellationToken);
    }

    public Task<CashInCallbackTransaction?> VoidAsync(
        PaymobVoidRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        return broker.VoidTransactionAsync(new(request.TransactionId), cancellationToken);
    }

    public Task<CashInCallbackTransaction?> RefundAsync(
        PaymobRefundRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var amountCents = (long)Math.Ceiling(request.Amount * 100);

        return broker.RefundTransactionAsync(
            new(request.TransactionId, amountCents.ToString(CultureInfo.InvariantCulture)),
            cancellationToken
        );
    }

    #region Helpers

    private async Task<(int OrderId, string PaymentKey)> _StartAsync(
        PaymobCashInCustomerData customer,
        decimal amount,
        int integrationId,
        int expiration,
        string? merchantOrderId,
        CancellationToken cancellationToken
    )
    {
        var amountCents = (long)Math.Ceiling(amount * 100);
        var orderResponse = await _CreateOrderAsync(amountCents, merchantOrderId, cancellationToken)
            .ConfigureAwait(false);

        var paymentKeyResponse = await _CreatePaymentKeyAsync(
                customer,
                integrationId,
                orderResponse.Id,
                amountCents,
                cancellationToken,
                expiration
            )
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(paymentKeyResponse.PaymentKey))
        {
            logger.LogEmptyPaymentKeyReceived();

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return (orderResponse.Id, paymentKeyResponse.PaymentKey);
    }

    private async Task<CashInPaymentKeyResponse> _CreatePaymentKeyAsync(
        PaymobCashInCustomerData customer,
        int integrationId,
        int orderId,
        long amountCents,
        CancellationToken cancellationToken,
        int expiration = 3600
    )
    {
        var billingData = new CashInBillingData(
            customer.FirstName,
            customer.LastName,
            customer.PhoneNumber,
            customer.Email
        );

        var request = new CashInPaymentKeyRequest(
            integrationId: integrationId,
            orderId: orderId,
            billingData,
            amountCents,
            lockOrderWhenPaid: true,
            expiration: expiration
        );

        try
        {
            return await broker.RequestPaymentKeyAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogCannotCreatePaymentKey(e, request.OrderId, request.IntegrationId, request.AmountCents);

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }
    }

    private async Task<CashInCreateOrderResponse> _CreateOrderAsync(
        long amountCents,
        string? merchantOrderId,
        CancellationToken cancellationToken
    )
    {
        var request = CashInCreateOrderRequest.CreateOrder(amountCents, merchantOrderId: merchantOrderId);

        CashInCreateOrderResponse response;

        try
        {
            response = await broker.CreateOrderAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogCannotCreateOrder(e, amountCents);

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return response;
    }

    #endregion
}
