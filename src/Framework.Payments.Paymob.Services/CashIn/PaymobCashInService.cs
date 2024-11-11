using Framework.Kernel.Primitives;
using Framework.Payments.Paymob.CashIn;
using Framework.Payments.Paymob.CashIn.Models.Orders;
using Framework.Payments.Paymob.CashIn.Models.Payment;
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
    Task<PaymobWalletCashInResponse> StartAsync(PaymobWalletCashInRequest request);

    [Pure]
    Task<PaymobAcceptCashInResponse> StartAsync(PaymobKioskCashInRequest request);
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
            request.ExpirationSeconds
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
            request.ExpirationSeconds
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

        if (string.IsNullOrWhiteSpace(payResponse.RedirectUrl))
        {
            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return new PaymobWalletCashInResponse(
            RedirectUrl: payResponse.RedirectUrl,
            OrderId: orderId.ToString(CultureInfo.InvariantCulture),
            Expiration: request.ExpirationSeconds
        );
    }

    public async Task<PaymobAcceptCashInResponse> StartAsync(PaymobKioskCashInRequest request)
    {
        var (orderId, paymentKey) = await _StartAsync(
            request.Customer,
            request.Amount,
            request.KioskIntegrationId,
            request.ExpirationSeconds
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

        if (payResponse.Data is null)
        {
            throw new ConflictException(PaymobMessageDescriptor.CashIn.ProviderConnectionFailed());
        }

        return new PaymobAcceptCashInResponse(
            BillingReference: payResponse.Data.BillReference.ToString(CultureInfo.InvariantCulture),
            OrderId: orderId.ToString(CultureInfo.InvariantCulture),
            Expiration: request.ExpirationSeconds
        );
    }

    #region Helpers

    private async Task<(int OrderId, string PaymentKey)> _StartAsync(
        PaymobCashInCustomerData customer,
        decimal amount,
        int integrationId,
        int expiration
    )
    {
        var amountCents = (int)Math.Ceiling(amount * 100);
        var orderResponse = await _CreateOrderAsync(amountCents);

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

    private async Task<CashInCreateOrderResponse> _CreateOrderAsync(int amountCents)
    {
        var request = CashInCreateOrderRequest.CreateOrder(amountCents);

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
