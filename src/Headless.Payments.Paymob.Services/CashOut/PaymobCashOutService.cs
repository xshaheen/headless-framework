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

public interface ICashOutService
{
    [Pure]
    Task<CashOutResult<CashOutResponse>> DisburseAsync(VodafoneCashOutRequest request);

    [Pure]
    Task<CashOutResult<CashOutResponse>> DisburseAsync(EtisalatCashOutRequest request);

    [Pure]
    Task<CashOutResult<CashOutResponse>> DisburseAsync(OrangeCashOutRequest request);

    [Pure]
    Task<CashOutResult<CashOutResponse>> DisburseAsync(BankWalletCashOutRequest request);

    [Pure]
    Task<CashOutResult<CashOutResponse>> DisburseAsync(BankAccountCashOutRequest request);

    [Pure]
    Task<CashOutResult<KioskCashOutResponse>> DisburseAsync(KioskCashOutRequest request);
}

public sealed class PaymobCashOutService(IPaymobCashOutBroker broker, ILogger<PaymobCashOutService> logger)
    : ICashOutService
{
    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(VodafoneCashOutRequest request)
    {
        var disburseRequest = CashOutDisburseRequest.Vodafone(request.Amount, request.PhoneNumber);
        var result = await _CoreDisburseAsync(disburseRequest);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(EtisalatCashOutRequest request)
    {
        var disburseRequest = CashOutDisburseRequest.Etisalat(request.Amount, request.PhoneNumber);
        var result = await _CoreDisburseAsync(disburseRequest);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(OrangeCashOutRequest request)
    {
        var disburseRequest = CashOutDisburseRequest.Orange(request.Amount, request.PhoneNumber, request.FullName);
        var result = await _CoreDisburseAsync(disburseRequest);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(BankWalletCashOutRequest request)
    {
        var disburseRequest = CashOutDisburseRequest.BankWallet(request.Amount, request.PhoneNumber, request.FullName);
        var result = await _CoreDisburseAsync(disburseRequest);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<CashOutResponse>> DisburseAsync(BankAccountCashOutRequest request)
    {
        var transactionType = _ConvertToString(request.Type);

        var disburseRequest = CashOutDisburseRequest.BankCard(
            request.Amount,
            request.AccountNumber,
            request.BankCode,
            transactionType,
            request.FullName
        );

        var result = await _CoreDisburseAsync(disburseRequest);

        return _ToCashOutCashOutResult(result);
    }

    public async Task<CashOutResult<KioskCashOutResponse>> DisburseAsync(KioskCashOutRequest request)
    {
        var disburseRequest = CashOutDisburseRequest.Accept(
            request.Amount,
            request.PhoneNumber,
            request.FirstName,
            request.LastName,
            request.Email
        );

        var result = await _CoreDisburseAsync(disburseRequest);

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

    private async Task<CashOutResult<CashOutTransaction>> _CoreDisburseAsync(CashOutDisburseRequest request)
    {
        CashOutTransaction result;

        try
        {
            result = await broker.Disburse(request);
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
