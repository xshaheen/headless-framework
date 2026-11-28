// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.Payments.Paymob.CashIn;

/// <summary>
/// The result of a CashIn operation: succeeded, rejected, or unknown. A Paymob failure never throws.
/// </summary>
/// <typeparam name="T">The success payload type.</typeparam>
/// <remarks>
/// Branch on <see cref="Outcome"/>. There is no single "failed" flag, so a caller cannot treat an
/// <see cref="PaymobCashInOutcome.Unknown"/> charge, refund, or void as a rejected one and move the money twice.
/// Never send that request again until the transaction callback or <c>IPaymobCashInBroker.GetOrderAsync</c> with
/// <see cref="OrderId"/> shows that the first one did not apply.
/// </remarks>
[PublicAPI]
public sealed class CashInResult<T>
{
    internal CashInResult(PaymobCashInOutcome outcome, T? data, ErrorDescriptor? error, string? orderId)
    {
        Outcome = outcome;
        Data = data;
        Error = error;
        OrderId = orderId;
        IsSucceeded = outcome is PaymobCashInOutcome.Succeeded;
        IsRejected = outcome is PaymobCashInOutcome.Rejected;
        IsUnknown = outcome is PaymobCashInOutcome.Unknown;
    }

    /// <summary>Gets what Paymob did with the request.</summary>
    public PaymobCashInOutcome Outcome { get; }

    /// <summary>Gets whether Paymob accepted the request, either completed or pending.</summary>
    [MemberNotNullWhen(true, nameof(Data))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSucceeded { get; }

    /// <summary>Gets whether no money moved. You may correct the request and send it again.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsRejected { get; }

    /// <summary>Gets whether the money may have moved. Resolve the transaction before you send it again.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsUnknown { get; }

    /// <summary>Gets the operation data. Set when <see cref="IsSucceeded"/> is <see langword="true"/>.</summary>
    public T? Data { get; }

    /// <summary>Gets a localized, code-tagged error. Set when <see cref="IsSucceeded"/> is <see langword="false"/>.</summary>
    public ErrorDescriptor? Error { get; }

    /// <summary>
    /// Gets the Paymob order the failed payment belongs to. Set on a rejected or unknown payment once its order
    /// exists; <see langword="null"/> on success (read the order from <see cref="Data"/>) and when no order was
    /// created. Pass it to <c>IPaymobCashInBroker.GetOrderAsync</c> to resolve an unknown outcome.
    /// </summary>
    public string? OrderId { get; }
}

/// <summary>Creates <see cref="CashInResult{T}"/> values, for example to fake the service in a test.</summary>
[PublicAPI]
public static class CashInResult
{
    /// <summary>Creates a result for a request Paymob accepted.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="data">The success payload.</param>
    /// <returns>A succeeded result.</returns>
    public static CashInResult<T> Success<T>(T data)
    {
        Argument.IsNotNull(data);

        return new(PaymobCashInOutcome.Succeeded, data, error: null, orderId: null);
    }

    /// <summary>Creates a result for a request that moved no money.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="error">Why the request was rejected.</param>
    /// <param name="orderId">The Paymob order the payment belongs to, when one was created.</param>
    /// <returns>A rejected result.</returns>
    public static CashInResult<T> Rejected<T>(ErrorDescriptor error, string? orderId = null)
    {
        Argument.IsNotNull(error);

        return new(PaymobCashInOutcome.Rejected, data: default, error, orderId);
    }

    /// <summary>Creates a result for a request that may have moved money.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="error">Why the outcome is unknown.</param>
    /// <param name="orderId">The Paymob order the payment belongs to, when one was created.</param>
    /// <returns>An unknown result.</returns>
    public static CashInResult<T> Unknown<T>(ErrorDescriptor error, string? orderId = null)
    {
        Argument.IsNotNull(error);

        return new(PaymobCashInOutcome.Unknown, data: default, error, orderId);
    }
}
