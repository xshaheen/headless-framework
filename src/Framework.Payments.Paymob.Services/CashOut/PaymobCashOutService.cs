// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using Framework.Kernel.Primitives;
using Framework.Payments.Paymob.CashOut;
using Framework.Payments.Paymob.CashOut.Models;
using Framework.Payments.Paymob.Services.CashOut.Requests;
using Framework.Payments.Paymob.Services.CashOut.Responses;
using Framework.Payments.Paymob.Services.Resources;
using Microsoft.Extensions.Logging;

namespace Framework.Payments.Paymob.Services.CashOut;

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
    private static readonly JsonSerializerOptions _Options =
        new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

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
            return CashOutResult<KioskCashOutResponse>.Failure(result.Error, result.Response);
        }

        if (result.Data.AmanCashingDetails?.BillingReference is null)
        {
            logger.LogCritical("Unexpected response to Accept CashOut {Response}", result.Data);
            return CashOutResult<KioskCashOutResponse>.Failure(
                PaymobMessageDescriptor.CashOut.ProviderConnectionFailed(),
                result.Response
            );
        }

        var status = result.Data.IsSuccess() ? CashOutResponseStatus.Success : CashOutResponseStatus.Pending;
        var billingReference = result.Data.AmanCashingDetails.BillingReference.Value.ToString(
            CultureInfo.InvariantCulture
        );
        var data = new KioskCashOutResponse(result.Data.TransactionId!, status, billingReference);

        return CashOutResult<KioskCashOutResponse>.Success(data, result.Response);
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
            logger.LogCritical(e, "Failed to start cash out {Response}", e.Body);
            return CashOutResult<CashOutTransaction>.Failure(
                PaymobMessageDescriptor.CashOut.ProviderConnectionFailed(),
                response: null
            );
        }

        if (result.IsPending() || result.IsSuccess())
        {
            return CashOutResult<CashOutTransaction>.Success(result, JsonSerializer.Serialize(result, _Options));
        }

        return CashOutResult<CashOutTransaction>.Failure(_GetError(result), JsonSerializer.Serialize(result, _Options));
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
                logger.LogCritical("Cash out budget exceeded {Response}", result);
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
            return CashOutResult<CashOutResponse>.Failure(result.Error, result.Response);
        }

        var status = result.Data.IsSuccess() ? CashOutResponseStatus.Success : CashOutResponseStatus.Pending;
        var response = new CashOutResponse(result.Data.TransactionId!, status);

        return CashOutResult<CashOutResponse>.Success(response, result.Response);
    }

    #endregion
}
