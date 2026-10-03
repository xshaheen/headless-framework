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
[PublicAPI]
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
