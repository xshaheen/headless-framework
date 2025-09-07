// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Exceptions;
using Framework.Payments.Paymob.CashIn;
using Framework.Payments.Paymob.CashIn.Models.Callback;
using Framework.Payments.Paymob.CashIn.Models.Orders;
using Framework.Payments.Paymob.CashIn.Models.Payment;
using Framework.Payments.Paymob.CashIn.Models.Refunds;
using Framework.Payments.Paymob.Services.CashIn.Requests;
using Framework.Payments.Paymob.Services.CashIn.Responses;
using Framework.Payments.Paymob.Services.Resources;
using Microsoft.Extensions.Logging;

namespace Framework.Payments.Paymob.Services.CashIn;

public interface IPaymobCashInService
{
    [Pure]
    Task<PaymobCardCashInResponse> StartAsync(PaymobCardCashInRequest request);

    [Pure]
    Task<PaymobCardSavedTokenCashInResponse> StartAsync(PaymobCardSavedTokenCashInRequest request);

    [Pure]
    Task<PaymobWalletCashInResponse> StartAsync(PaymobWalletCashInRequest request);

    [Pure]
    Task<PaymobKioskCashInResponse> StartAsync(PaymobKioskCashInRequest request);

    [Pure]
    Task<CashInCallbackTransaction?> RefundAsync(PaymobRefundRequest request);
}

public sealed class PaymobCashInService(IPaymobCashInBroker broker, ILogger<PaymobCashInService> logger)
    : IPaymobCashInService
{
    public async Task<PaymobCardCashInResponse> StartAsync(PaymobCardCashInRequest request)
    {
        var (orderId, paymentKey) = await _StartAsync(
            request.Customer,
            request.Amount,
            request.CardIntegrationId,
            request.ExpirationSeconds,
            request.MerchantOrderId
        );

        return new PaymobCardCashInResponse(
            IframeSrc: broker.CreateIframeSrc(iframeId: request.IframeSrc, token: paymentKey),
            PaymentKey: paymentKey,
            OrderId: orderId.ToString(CultureInfo.InvariantCulture),
            Expiration: request.ExpirationSeconds
        );
    }

    public async Task<PaymobWalletCashInResponse> StartAsync(PaymobWalletCashInRequest request)
    {
        var (orderId, paymentKey) = await _StartAsync(
            request.Customer,
            request.Amount,
            request.WalletIntegrationId,
            request.ExpirationSeconds,
            request.MerchantOrderId
        );

        CashInWalletPayResponse payResponse;

        try
        {
            payResponse = await broker.CreateWalletPayAsync(paymentKey, request.WalletPhoneNumber);
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Failed to create wallet cash in. {Request}", request);

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

    public async Task<PaymobKioskCashInResponse> StartAsync(PaymobKioskCashInRequest request)
    {
        var (orderId, paymentKey) = await _StartAsync(
            request.Customer,
            request.Amount,
            request.KioskIntegrationId,
            request.ExpirationSeconds,
            request.MerchantOrderId
        );

        CashInKioskPayResponse payResponse;

        try
        {
            payResponse = await broker.CreateKioskPayAsync(paymentKey);
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Failed to create wallet cash in. {Request}", request);

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

    public async Task<PaymobCardSavedTokenCashInResponse> StartAsync(PaymobCardSavedTokenCashInRequest request)
    {
        var (orderId, paymentKey) = await _StartAsync(
            request.Customer,
            request.Amount,
            request.SavedTokenIntegrationId,
            request.ExpirationSeconds,
            request.MerchantOrderId
        );

        CashInSavedTokenPayResponse payResponse;

        try
        {
            payResponse = await broker.CreateSavedTokenPayAsync(paymentKey, request.CardToken);
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Failed to create saved token cash in. {Request}", request);

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        if (payResponse.ErrorOccured.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return new PaymobCardSavedTokenCashInResponse(
            IsSuccess: payResponse.Success.Equals("true", StringComparison.OrdinalIgnoreCase),
            OrderId: orderId.ToString(CultureInfo.InvariantCulture)
        );
    }

    public Task<CashInCallbackTransaction?> RefundAsync(PaymobVoidRequest request)
    {
        Argument.IsNotNull(request);

        return broker.VoidTransactionAsync(new(request.TransactionId));
    }

    public Task<CashInCallbackTransaction?> RefundAsync(PaymobRefundRequest request)
    {
        Argument.IsNotNull(request);

        var amountCents = (int)Math.Ceiling(request.Amount * 100);

        return broker.RefundTransactionAsync(
            new(request.TransactionId, amountCents.ToString(CultureInfo.InvariantCulture))
        );
    }

    #region Helpers

    private async Task<(int OrderId, string PaymentKey)> _StartAsync(
        PaymobCashInCustomerData customer,
        decimal amount,
        int integrationId,
        int expiration,
        string? merchantOrderId
    )
    {
        var amountCents = (int)Math.Ceiling(amount * 100);
        var orderResponse = await _CreateOrderAsync(amountCents, merchantOrderId);

        var paymentKeyResponse = await _CreatePaymentKeyAsync(
            customer,
            integrationId,
            orderResponse.Id,
            amountCents,
            expiration
        );

        if (string.IsNullOrWhiteSpace(paymentKeyResponse.PaymentKey))
        {
            logger.LogCritical("Empty payment key received when request CashIn payment key");

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return (orderResponse.Id, paymentKeyResponse.PaymentKey);
    }

    private async Task<CashInPaymentKeyResponse> _CreatePaymentKeyAsync(
        PaymobCashInCustomerData customer,
        int integrationId,
        int orderId,
        int amountCents,
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
            return await broker.RequestPaymentKeyAsync(request);
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Can't create CashIn payment key. {@Request}", request);

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }
    }

    private async Task<CashInCreateOrderResponse> _CreateOrderAsync(int amountCents, string? merchantOrderId)
    {
        var request = CashInCreateOrderRequest.CreateOrder(amountCents, merchantOrderId: merchantOrderId);

        CashInCreateOrderResponse response;

        try
        {
            response = await broker.CreateOrderAsync(request);
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Can't create CashIn order. {AmountCents}", amountCents);

            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return response;
    }

    #endregion
}
