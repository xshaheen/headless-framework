// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Payments.Paymob.CashIn.Models.Constants;

namespace Framework.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInOrdersPageRequest
{
    private CashInOrdersPageRequest() { }

    public Dictionary<string, string> Query { get; } = new(StringComparer.Ordinal);

    public static CashInOrdersPageRequest Create => new();

    public CashInOrdersPageRequest WithIndex(int index)
    {
        Query["page"] = index.ToString(CultureInfo.InvariantCulture);
        return this;
    }

    public CashInOrdersPageRequest WithPageSize(int size)
    {
        Query["page_size"] = size.ToString(CultureInfo.InvariantCulture);
        return this;
    }

    public CashInOrdersPageRequest WithOrderId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            Query["order_id"] = id;
        }

        return this;
    }

    public CashInOrdersPageRequest WithMerchantOrderId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            Query["merchant_order_id"] = id;
        }

        return this;
    }

    public CashInOrdersPageRequest WithCurrency(string currency)
    {
        if (!string.IsNullOrWhiteSpace(currency))
        {
            Query["currency"] = currency;
        }

        return this;
    }

    public CashInOrdersPageRequest WithIsLive(bool isLive)
    {
        Query["transaction_id"] = isLive ? "true" : "false";
        return this;
    }

    public CashInOrdersPageRequest WithIsDeliveryNeeded(bool isNeeded)
    {
        Query["delivery_needed"] = isNeeded ? "true" : "false";
        return this;
    }

    public CashInOrdersPageRequest WithAmountFilter(int? from, int? to)
    {
        if (from.HasValue && to.HasValue)
        {
            Argument.Range(from.Value, to.Value);
        }

        if (from is not null)
        {
            Query["amount_from"] = @from.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (to is not null)
        {
            Query["amount_to"] = to.Value.ToString(CultureInfo.InvariantCulture);
        }

        return this;
    }

    public CashInOrdersPageRequest WithPaidAmountFilter(int? from, int? to)
    {
        if (from.HasValue && to.HasValue)
        {
            Argument.Range(from.Value, to.Value);
        }

        if (from is not null)
        {
            Query["paid_amount_from"] = @from.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (to is not null)
        {
            Query["paid_amount_to"] = to.Value.ToString(CultureInfo.InvariantCulture);
        }

        return this;
    }

    /// <summary>Append api source to the query</summary>
    /// <param name="origin">See: <see cref="CashInOrderApiSource"/></param>
    public CashInOrdersPageRequest WithApiSource(string origin)
    {
        if (!string.IsNullOrWhiteSpace(origin))
        {
            Query["api_source"] = origin;
        }

        return this;
    }

    /// <summary>Append status to the query.</summary>
    /// <param name="status">See: <see cref="CashInOrderStatuses"/></param>
    public CashInOrdersPageRequest WithStatus(string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            Query["status"] = status;
        }

        return this;
    }
}
