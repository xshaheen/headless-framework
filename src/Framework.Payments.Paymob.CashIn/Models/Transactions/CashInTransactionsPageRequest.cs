// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models.Constants;

namespace Framework.Payments.Paymob.CashIn.Models.Transactions;

[PublicAPI]
public sealed class CashInTransactionsPageRequest
{
    private CashInTransactionsPageRequest() { }

    public Dictionary<string, string> Query { get; } = new(StringComparer.Ordinal);

    public static CashInTransactionsPageRequest Create => new();

    public CashInTransactionsPageRequest WithIndex(int index)
    {
        Query["page"] = index.ToString(CultureInfo.InvariantCulture);

        return this;
    }

    public CashInTransactionsPageRequest WithPageSize(int size)
    {
        Query["page_size"] = size.ToString(CultureInfo.InvariantCulture);

        return this;
    }

    public CashInTransactionsPageRequest WithOrderId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            Query["order_id"] = id;
        }

        return this;
    }

    public CashInTransactionsPageRequest WithMerchantOrderId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            Query["merchant_order_id"] = id;
        }

        return this;
    }

    public CashInTransactionsPageRequest WithIntegrationId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            Query["integration_id"] = id;
        }

        return this;
    }

    public CashInTransactionsPageRequest WithTransactionId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            Query["transaction_id"] = id;
        }

        return this;
    }

    public CashInTransactionsPageRequest WithIsLive(bool isLive)
    {
        Query["is_live"] = isLive ? "true" : "false";

        return this;
    }

    public CashInTransactionsPageRequest WithIsInstallment(bool isInstallment)
    {
        Query["is_installment"] = isInstallment ? "true" : "false";

        return this;
    }

    public CashInTransactionsPageRequest WithAmountFilter(int? from, int? to)
    {
        if (from > to)
        {
            throw new ArgumentException(
                $"parameter 'from' must be less than parameter 'to' (from: {from.Value.ToString(CultureInfo.InvariantCulture)}, to: {to.Value.ToString(CultureInfo.InvariantCulture)}).",
                nameof(from)
            );
        }

        if (from is not null)
        {
            Query["amount_from"] = from.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (to is not null)
        {
            Query["amount_to"] = to.Value.ToString(CultureInfo.InvariantCulture);
        }

        return this;
    }

    /// <summary>Add transaction_type to the query</summary>
    /// <param name="type">See: <see cref="CashInTransactionTypes"/></param>
    public CashInTransactionsPageRequest WithTransactionType(string type)
    {
        if (!string.IsNullOrWhiteSpace(type))
        {
            Query["transaction_type"] = type;
        }

        return this;
    }

    /// <summary>Add origin to the query</summary>
    /// <param name="origin">See: <see cref="CashInTransactionOrigins"/></param>
    public CashInTransactionsPageRequest WithOrigin(string origin)
    {
        if (!string.IsNullOrWhiteSpace(origin))
        {
            Query["origin"] = origin;
        }

        return this;
    }

    /// <summary>Add source to the query</summary>
    /// <param name="source">See: <see cref="CashInTransactionSources"/></param>
    public CashInTransactionsPageRequest WithSource(string source)
    {
        if (!string.IsNullOrWhiteSpace(source))
        {
            Query["source"] = source;
        }

        return this;
    }

    /// <summary>Add status to the query</summary>
    /// <param name="status">See: <see cref="CashInStatuses"/></param>
    public CashInTransactionsPageRequest WithStatus(string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            Query["status"] = status;
        }

        return this;
    }
}
