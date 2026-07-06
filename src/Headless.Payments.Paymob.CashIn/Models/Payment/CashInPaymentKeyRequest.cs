// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.CashIn.Models.Payment;

public sealed class CashInPaymentKeyRequest
{
    public CashInPaymentKeyRequest(
        int integrationId,
        int orderId,
        CashInBillingData billingData,
        long amountCents,
        string currency = "EGP",
        bool lockOrderWhenPaid = true,
        int? expiration = null
    )
    {
        if (expiration is not null)
        {
            Argument.IsPositive(expiration.Value);
        }

        Argument.IsPositive(integrationId);
        Argument.IsPositive(orderId);
        Argument.IsNotNull(billingData);
        Argument.IsPositive(amountCents);
        Argument.IsNotNullOrEmpty(currency);

        IntegrationId = integrationId;
        OrderId = orderId;
        AmountCents = amountCents;
        Currency = currency;
        Expiration = expiration;
        LockOrderWhenPaid = lockOrderWhenPaid;
        BillingData = billingData;
    }

    public int IntegrationId { get; }

    public int OrderId { get; }

    /// <summary>The amount in the smallest currency unit (integer cents), e.g. <c>10000</c> for 100.00 EGP.</summary>
    public long AmountCents { get; }

    public string Currency { get; }

    public int? Expiration { get; }

    public bool LockOrderWhenPaid { get; }

    public CashInBillingData BillingData { get; }
}
