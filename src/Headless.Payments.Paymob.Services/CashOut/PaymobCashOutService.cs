// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Internals;
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
/// <c>Disburse</c> method, and maps the raw response to a domain result. Transport errors are
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

internal sealed class PaymobCashOutService(IPaymobCashOutBroker broker, ILogger<PaymobCashOutService> logger)
    : ICashOutService
{
    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(
        VodafoneCashOutRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var disburseRequest = CashOutDisburseRequest.Vodafone(request.Amount, request.PhoneNumber);
        var result = await _CoreDisburseAsync(disburseRequest, cancellationToken).ConfigureAwait(false);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(
        EtisalatCashOutRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var disburseRequest = CashOutDisburseRequest.Etisalat(request.Amount, request.PhoneNumber);
        var result = await _CoreDisburseAsync(disburseRequest, cancellationToken).ConfigureAwait(false);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(
        OrangeCashOutRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var disburseRequest = CashOutDisburseRequest.Orange(request.Amount, request.PhoneNumber, request.FullName);
        var result = await _CoreDisburseAsync(disburseRequest, cancellationToken).ConfigureAwait(false);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(
        BankWalletCashOutRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var disburseRequest = CashOutDisburseRequest.BankWallet(request.Amount, request.PhoneNumber, request.FullName);
        var result = await _CoreDisburseAsync(disburseRequest, cancellationToken).ConfigureAwait(false);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(
        BankAccountCashOutRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var transactionType = _ConvertToString(request.Type);

        var disburseRequest = CashOutDisburseRequest.BankCard(
            request.Amount,
            request.AccountNumber,
            request.BankCode,
            transactionType,
            request.FullName
        );

        var result = await _CoreDisburseAsync(disburseRequest, cancellationToken).ConfigureAwait(false);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<KioskCashOutResponse>> DisburseAsync(
        KioskCashOutRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var disburseRequest = CashOutDisburseRequest.Accept(
            request.Amount,
            request.PhoneNumber,
            request.FirstName,
            request.LastName,
            request.Email
        );

        var result = await _CoreDisburseAsync(disburseRequest, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return CashOutResult.Failure<KioskCashOutResponse>(result.Error, result.Response);
        }

        if (result.Data.AmanCashingDetails?.BillingReference is null)
        {
            logger.LogUnexpectedAcceptCashOutResponse(result.Data);

            return CashOutResult.Failure<KioskCashOutResponse>(
                PaymobMessageDescriptor.CashOut.ProviderConnectionFailed(),
                result.Response
            );
        }

        var status = result.Data.IsSuccess() ? CashOutResponseStatus.Success : CashOutResponseStatus.Pending;
        var billingReference = result.Data.AmanCashingDetails.BillingReference.Value.ToString(
            CultureInfo.InvariantCulture
        );
        var data = new KioskCashOutResponse(result.Data.TransactionId!, status, billingReference);

        return CashOutResult.Success(data, result.Response);
    }

    #region Helpers

    private async Task<CashOutResult<CashOutTransaction>> _CoreDisburseAsync(
        CashOutDisburseRequest request,
        CancellationToken cancellationToken
    )
    {
        CashOutTransaction result;

        try
        {
            result = await broker.Disburse(request, cancellationToken).ConfigureAwait(false);
        }
        catch (PaymobCashOutException e)
        {
            logger.LogFailedToStartCashOut(e, e.Body);
            return CashOutResult.Failure<CashOutTransaction>(
                PaymobMessageDescriptor.CashOut.ProviderConnectionFailed(),
                response: null
            );
        }

        var json = JsonSerializer.Serialize(result, CashOutJsonOptions.JsonOptions);

        if (result.IsPending() || result.IsSuccess())
        {
            return CashOutResult.Success(result, json);
        }

        return CashOutResult.Failure<CashOutTransaction>(_GetError(result), json);
    }

    private ErrorDescriptor _GetError(CashOutTransaction result)
    {
        if (result.IsProviderDownError())
        {
            return PaymobMessageDescriptor.CashOut.ProviderIsDown();
        }

        if (result.IsNotHaveVodafoneCashError())
        {
            return PaymobMessageDescriptor.General.NoVodafoneCash();
        }

        if (result.IsNotHaveEtisalatCashError())
        {
            return PaymobMessageDescriptor.General.NoEtisalatCash();
        }

        if (result.IsRequestValidationError())
        {
            // TO DO: Get exact validation is IBAN invalid for example or what is wrong
            // NOTE: StatusDescription can has multi form:
            // "status_description": { "msisdn": ["Phonenumbers entered are incorrect"] }
            // "status_description": { "amount": ["This field is required."]},
            // And one another form when just mess one field like bank_code
            // "status_description": { "non_field_errors": ["You must pass valid values for fields [bank_code, bank_card_number, bank_transaction_type, full_name]"] }

            if (
                result.StatusDescription is string description
                && description.Contains(
                    "the amount to be disbursed exceeds you budget limit",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                logger.LogCashOutBudgetExceeded(result);
                return PaymobMessageDescriptor.CashOut.ProviderConnectionFailed();
            }

            return PaymobMessageDescriptor.CashOut.InvalidRequest();
        }

        return PaymobMessageDescriptor.CashOut.ProviderConnectionFailed();
    }

    private static string _ConvertToString(BankTransactionType type)
    {
        return type switch
        {
            BankTransactionType.CashTransfer => BankTransactionTypes.CashTransfer,
            BankTransactionType.CreditCard => BankTransactionTypes.CreditCard,
            BankTransactionType.Prepaid => BankTransactionTypes.PrepaidCard,
            BankTransactionType.Salary => BankTransactionTypes.Salary,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };
    }

    private static CashOutResult<CashOutResponse> _ToCashOutCashOutResult(CashOutResult<CashOutTransaction> result)
    {
        if (!result.Succeeded)
        {
            return CashOutResult.Failure<CashOutResponse>(result.Error, result.Response);
        }

        var status = result.Data.IsSuccess() ? CashOutResponseStatus.Success : CashOutResponseStatus.Pending;
        var response = new CashOutResponse(result.Data.TransactionId!, status);

        return CashOutResult.Success(response, result.Response);
    }

    #endregion
}

internal static partial class PaymobCashOutLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "UnexpectedAcceptCashOutResponse",
        Level = LogLevel.Critical,
        Message = "Unexpected response to Accept CashOut {Response}"
    )]
    public static partial void LogUnexpectedAcceptCashOutResponse(this ILogger logger, CashOutTransaction? response);

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToStartCashOut",
        Level = LogLevel.Critical,
        Message = "Failed to start cash out {Response}"
    )]
    public static partial void LogFailedToStartCashOut(this ILogger logger, Exception exception, string? response);

    [LoggerMessage(
        EventId = 3,
        EventName = "CashOutBudgetExceeded",
        Level = LogLevel.Critical,
        Message = "Cash out budget exceeded {Response}"
    )]
    public static partial void LogCashOutBudgetExceeded(this ILogger logger, CashOutTransaction response);
}
