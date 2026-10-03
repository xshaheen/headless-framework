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

/// <summary>
/// High-level service that orchestrates Paymob CashOut disbursements across multiple channels:
/// mobile wallets (Vodafone, Etisalat, Orange), bank wallets, bank accounts, and Aman kiosks.
/// </summary>
/// <remarks>
/// <para>
/// Each overload builds the appropriate <c>CashOutDisburseRequest</c>, calls the broker's
/// <c>DisburseAsync</c> method, and maps the raw response to a domain result. Transport errors are
/// caught and returned as <c>CashOutResult.Failure</c> with a structured error descriptor rather
/// than propagated as exceptions, so callers do not need try/catch for Paymob API failures.
/// </para>
/// <para>
/// Inspect <c>CashOutResult.Succeeded</c> to determine the outcome. When false, <c>Error</c>
/// carries a localised, code-tagged descriptor. When true, <c>Data</c> contains the transaction
/// ID and status.
/// </para>
/// </remarks>
[PublicAPI]
public interface ICashOutService
{
    /// <summary>Disburses funds to a Vodafone Cash mobile wallet.</summary>
    /// <param name="request">Recipient phone number and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A result wrapping the transaction ID and disbursement status, or an error descriptor on failure.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        VodafoneCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disburses funds to an Etisalat Cash mobile wallet.</summary>
    /// <param name="request">Recipient phone number and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A result wrapping the transaction ID and disbursement status, or an error descriptor on failure.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        EtisalatCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disburses funds to an Orange Money mobile wallet.</summary>
    /// <param name="request">Recipient phone number, full name, and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A result wrapping the transaction ID and disbursement status, or an error descriptor on failure.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        OrangeCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disburses funds to a bank-linked mobile wallet.</summary>
    /// <param name="request">Recipient phone number, full name, and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A result wrapping the transaction ID and disbursement status, or an error descriptor on failure.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        BankWalletCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disburses funds directly to a bank account or card.</summary>
    /// <param name="request">Recipient account number, bank code, transaction type, full name, and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A result wrapping the transaction ID and disbursement status, or an error descriptor on failure.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        BankAccountCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Disburses funds via the Aman kiosk (Accept) channel. The recipient collects cash at any
    /// Aman outlet using the billing reference in the response.
    /// </summary>
    /// <param name="request">Recipient personal details (name, email, phone) and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A result wrapping the transaction ID, disbursement status, and Aman billing reference,
    /// or an error descriptor on failure.
    /// </returns>
    Task<CashOutResult<KioskCashOutResponse>> DisburseAsync(
        KioskCashOutRequest request,
        CancellationToken cancellationToken = default
    );
}
