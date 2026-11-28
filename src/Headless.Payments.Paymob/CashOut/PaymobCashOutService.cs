// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashOut.Internal;
using Headless.Payments.Paymob.Resources;
using Headless.Primitives;
using Microsoft.Extensions.Logging;

namespace Headless.Payments.Paymob.CashOut;

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

        if (!result.IsSucceeded)
        {
            return _Forward<CashOutTransaction, KioskCashOutResponse>(result);
        }

        if (result.Data.AmanCashingDetails?.BillingReference is null)
        {
            logger.LogUnexpectedAcceptCashOutResponse(result.Data);

            // Paymob accepted the payout, so the money is committed even though the recipient has no reference to
            // collect it with. A second payout would pay twice; the caller must look the first one up instead.
            return CashOutResult.Unknown<KioskCashOutResponse>(
                PaymobMessageDescriptor.CashOut.OutcomeUnknown(),
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
        var result = await broker.DisburseAsync(request, cancellationToken).ConfigureAwait(false);

        var json = result.Value is null
            ? result.Error?.Body
            : JsonSerializer.Serialize(result.Value, CashOutJsonOptions.JsonOptions);

        if (result.IsSucceeded)
        {
            return CashOutResult.Success(result.Value, json!);
        }

        if (result.IsRejected)
        {
            // A parsed rejection names its reason; a bare 4xx, or a request that was never sent, does not.
            if (result.Value is not null)
            {
                return CashOutResult.Rejected<CashOutTransaction>(_GetError(result.Value), json);
            }

            logger.LogCashOutRejected(result.Error.Exception, result.Error.StatusCode, result.Error.Message);

            return CashOutResult.Rejected<CashOutTransaction>(
                PaymobMessageDescriptor.CashOut.ProviderConnectionFailed(),
                json
            );
        }

        logger.LogCashOutOutcomeUnknown(result.Error.Exception, result.Error.StatusCode, result.Error.Message);

        return CashOutResult.Unknown<CashOutTransaction>(PaymobMessageDescriptor.CashOut.OutcomeUnknown(), json);
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

            if (_DescribesBudgetExceeded(result.StatusDescription))
            {
                logger.LogCashOutBudgetExceeded(result);
                return PaymobMessageDescriptor.CashOut.ProviderConnectionFailed();
            }

            return PaymobMessageDescriptor.CashOut.InvalidRequest();
        }

        return PaymobMessageDescriptor.CashOut.ProviderConnectionFailed();
    }

    private static bool _DescribesBudgetExceeded(object? statusDescription)
    {
        // The deserializer fills an object-typed member with a JsonElement, never a string, so a description that
        // arrives from Paymob is JSON text; a string only appears in a transaction built by hand.
        var description = statusDescription switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };

        return description?.Contains(
            "the amount to be disbursed exceeds you budget limit",
            StringComparison.OrdinalIgnoreCase
        )
            is true;
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
        if (!result.IsSucceeded)
        {
            return _Forward<CashOutTransaction, CashOutResponse>(result);
        }

        var status = result.Data.IsSuccess() ? CashOutResponseStatus.Success : CashOutResponseStatus.Pending;
        var response = new CashOutResponse(result.Data.TransactionId!, status);

        return CashOutResult.Success(response, result.Response);
    }

    private static CashOutResult<TOut> _Forward<TIn, TOut>(CashOutResult<TIn> result)
    {
        return result.IsRejected
            ? CashOutResult.Rejected<TOut>(result.Error, result.Response)
            : CashOutResult.Unknown<TOut>(result.Error!, result.Response);
    }

    #endregion
}

/// <summary>
/// High-level service that orchestrates Paymob CashOut disbursements across multiple channels:
/// mobile wallets (Vodafone, Etisalat, Orange), bank wallets, bank accounts, and Aman kiosks.
/// </summary>
/// <remarks>
/// <para>
/// Each overload builds the appropriate <c>CashOutDisburseRequest</c>, calls the broker's
/// <c>DisburseAsync</c> method, and maps its outcome to a domain result. Paymob failures never throw.
/// </para>
/// <para>
/// Branch on <c>CashOutResult.Outcome</c>. <c>Succeeded</c> carries the transaction ID and status.
/// <c>Rejected</c> means nothing was paid, and <c>Error</c> says why. <c>Unknown</c>
/// (<c>cash_out:outcome_unknown</c>) means the payout may have gone through: resolve it with the payout
/// callback or <c>IPaymobCashOutBroker.GetTransactionsAsync</c> before you disburse again, or you can pay twice.
/// </para>
/// </remarks>
[PublicAPI]
public interface ICashOutService
{
    /// <summary>Disburses funds to a Vodafone Cash mobile wallet.</summary>
    /// <param name="request">Recipient phone number and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A succeeded result with the transaction ID and status, or a rejected or unknown result with an error descriptor.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        VodafoneCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disburses funds to an Etisalat Cash mobile wallet.</summary>
    /// <param name="request">Recipient phone number and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A succeeded result with the transaction ID and status, or a rejected or unknown result with an error descriptor.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        EtisalatCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disburses funds to an Orange Money mobile wallet.</summary>
    /// <param name="request">Recipient phone number, full name, and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A succeeded result with the transaction ID and status, or a rejected or unknown result with an error descriptor.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        OrangeCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disburses funds to a bank-linked mobile wallet.</summary>
    /// <param name="request">Recipient phone number, full name, and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A succeeded result with the transaction ID and status, or a rejected or unknown result with an error descriptor.</returns>
    Task<CashOutResult<CashOutResponse>> DisburseAsync(
        BankWalletCashOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disburses funds directly to a bank account or card.</summary>
    /// <param name="request">Recipient account number, bank code, transaction type, full name, and amount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A succeeded result with the transaction ID and status, or a rejected or unknown result with an error descriptor.</returns>
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

internal static partial class PaymobCashOutLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "UnexpectedAcceptCashOutResponse",
        Level = LogLevel.Critical,
        Message = "Unexpected response to Accept CashOut {Response}"
    )]
    internal static partial void LogUnexpectedAcceptCashOutResponse(this ILogger logger, CashOutTransaction? response);

    // Logs the provider HTTP status and the result summary (both free of PII) instead of the raw response body,
    // which may carry recipient PII or provider internal detail.
    [LoggerMessage(
        EventId = 2,
        EventName = "CashOutRejected",
        Level = LogLevel.Error,
        Message = "Cash out rejected. Provider responded with status {StatusCode}: {Reason}"
    )]
    internal static partial void LogCashOutRejected(
        this ILogger logger,
        Exception? exception,
        HttpStatusCode? statusCode,
        string reason
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "CashOutBudgetExceeded",
        Level = LogLevel.Critical,
        Message = "Cash out budget exceeded {Response}"
    )]
    internal static partial void LogCashOutBudgetExceeded(this ILogger logger, CashOutTransaction response);

    [LoggerMessage(
        EventId = 4,
        EventName = "CashOutOutcomeUnknown",
        Level = LogLevel.Critical,
        Message = "Cash out outcome unknown; the payout may have applied. Provider status {StatusCode}: {Reason}"
    )]
    internal static partial void LogCashOutOutcomeUnknown(
        this ILogger logger,
        Exception? exception,
        HttpStatusCode? statusCode,
        string reason
    );
}
