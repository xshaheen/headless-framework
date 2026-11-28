// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.Payments.Paymob.CashOut;

/// <summary>
/// The result of a CashOut disbursement: succeeded, rejected, or unknown. A Paymob failure never throws.
/// </summary>
/// <typeparam name="T">
/// The success payload type (<c>CashOutResponse</c> for most channels,
/// <c>KioskCashOutResponse</c> for Aman kiosk disbursements).
/// </typeparam>
/// <remarks>
/// Branch on <see cref="Outcome"/>. There is no single "failed" flag, so a caller cannot treat an
/// <see cref="PaymobCashOutOutcome.Unknown"/> payout as a rejected one and pay it twice. Never disburse again after
/// an unknown result until the payout callback or <c>IPaymobCashOutBroker.GetTransactionsAsync</c> shows that the
/// first payout did not apply.
/// </remarks>
[PublicAPI]
public sealed class CashOutResult<T>
{
    internal CashOutResult(PaymobCashOutOutcome outcome, T? data, ErrorDescriptor? error, string? response)
    {
        Outcome = outcome;
        Data = data;
        Error = error;
        Response = response;
        IsSucceeded = outcome is PaymobCashOutOutcome.Succeeded;
        IsRejected = outcome is PaymobCashOutOutcome.Rejected;
        IsUnknown = outcome is PaymobCashOutOutcome.Unknown;
    }

    /// <summary>Gets what Paymob did with the disbursement.</summary>
    public PaymobCashOutOutcome Outcome { get; }

    /// <summary>Gets whether Paymob accepted the disbursement, either paid or pending.</summary>
    [MemberNotNullWhen(true, nameof(Data), nameof(Response))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSucceeded { get; }

    /// <summary>Gets whether Paymob definitely did not pay. You may correct the request and send it again.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsRejected { get; }

    /// <summary>Gets whether the money may have moved. Resolve the payout before you disburse again.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsUnknown { get; }

    /// <summary>Gets the disbursement data. Set when <see cref="IsSucceeded"/> is <see langword="true"/>.</summary>
    public T? Data { get; }

    /// <summary>Gets a localized, code-tagged error. Set when <see cref="IsSucceeded"/> is <see langword="false"/>.</summary>
    public ErrorDescriptor? Error { get; }

    /// <summary>
    /// Gets the Paymob response as JSON, for logs and diagnostics. Set on success, and on another outcome when
    /// Paymob answered with a body. It may carry recipient data.
    /// </summary>
    public string? Response { get; }
}

/// <summary>Creates <see cref="CashOutResult{T}"/> values, for example to fake the service in a test.</summary>
[PublicAPI]
public static class CashOutResult
{
    /// <summary>Creates a result for a disbursement Paymob accepted.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="data">The success payload.</param>
    /// <param name="response">The Paymob response as JSON.</param>
    /// <returns>A succeeded result.</returns>
    public static CashOutResult<T> Success<T>(T data, string response)
    {
        Argument.IsNotNull(data);
        Argument.IsNotNull(response);

        return new(PaymobCashOutOutcome.Succeeded, data, error: null, response);
    }

    /// <summary>Creates a result for a disbursement Paymob definitely did not pay.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="error">Why the disbursement was rejected.</param>
    /// <param name="response">The Paymob response, if one arrived.</param>
    /// <returns>A rejected result.</returns>
    public static CashOutResult<T> Rejected<T>(ErrorDescriptor error, string? response)
    {
        Argument.IsNotNull(error);

        return new(PaymobCashOutOutcome.Rejected, data: default, error, response);
    }

    /// <summary>Creates a result for a disbursement that may have paid.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="error">Why the outcome is unknown.</param>
    /// <param name="response">The Paymob response, if one arrived.</param>
    /// <returns>An unknown result.</returns>
    public static CashOutResult<T> Unknown<T>(ErrorDescriptor error, string? response)
    {
        Argument.IsNotNull(error);

        return new(PaymobCashOutOutcome.Unknown, data: default, error, response);
    }
}
