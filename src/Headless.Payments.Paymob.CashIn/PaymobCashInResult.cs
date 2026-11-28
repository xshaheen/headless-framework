// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.CashIn;

/// <summary>The outcome of one Paymob CashIn operation.</summary>
/// <typeparam name="T">The parsed Paymob response type.</typeparam>
/// <remarks>
/// <para>
/// Branch on <see cref="Outcome"/> or call <see cref="Match{TResult}"/>, which makes you handle all three
/// outcomes. There is no single "failed" flag, so code cannot treat an <see cref="PaymobCashInOutcome.Unknown"/>
/// charge, refund, or void as a rejected one by accident.
/// </para>
/// <para>
/// Never send a money-moving request (a saved-card charge, a refund, or a void) again after
/// <see cref="PaymobCashInOutcome.Unknown"/> until the transaction callback, <c>IPaymobCashInBroker.GetOrderAsync</c>,
/// or <c>IPaymobCashInBroker.GetTransactionsPageAsync</c> shows that the first one did not apply.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PaymobCashInResult<T>
{
    internal PaymobCashInResult(PaymobCashInOutcome outcome, T? value, PaymobCashInResultError? error)
    {
        Outcome = outcome;
        Value = value;
        Error = error;
        IsSucceeded = outcome is PaymobCashInOutcome.Succeeded;
        IsRejected = outcome is PaymobCashInOutcome.Rejected;
        IsUnknown = outcome is PaymobCashInOutcome.Unknown;
    }

    /// <summary>Gets what Paymob did with the request.</summary>
    public PaymobCashInOutcome Outcome { get; }

    /// <summary>Gets whether Paymob accepted the request.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSucceeded { get; }

    /// <summary>Gets whether Paymob definitely did not apply the request.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsRejected { get; }

    /// <summary>Gets whether the request may have been applied. Resolve it before sending the request again.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsUnknown { get; }

    /// <summary>
    /// Gets the parsed Paymob response. It is always set when <see cref="IsSucceeded"/> is <see langword="true"/>,
    /// and also set on another outcome when Paymob answered with a readable body, such as a declined charge.
    /// </summary>
    public T? Value { get; }

    /// <summary>Gets why the outcome is rejected or unknown. Set whenever <see cref="IsSucceeded"/> is <see langword="false"/>.</summary>
    public PaymobCashInResultError? Error { get; }

    /// <summary>Invokes the function for the outcome and returns its result.</summary>
    /// <typeparam name="TResult">The type both branches produce.</typeparam>
    /// <param name="succeeded">Invoked with the parsed response when Paymob accepted the request.</param>
    /// <param name="rejected">Invoked with the error when Paymob definitely did not apply the request.</param>
    /// <param name="unknown">Invoked with the error when the request may have been applied.</param>
    /// <returns>The value the invoked function returned.</returns>
    public TResult Match<TResult>(
        Func<T, TResult> succeeded,
        Func<PaymobCashInResultError, TResult> rejected,
        Func<PaymobCashInResultError, TResult> unknown
    )
    {
        Argument.IsNotNull(succeeded);
        Argument.IsNotNull(rejected);
        Argument.IsNotNull(unknown);

        return Outcome switch
        {
            PaymobCashInOutcome.Succeeded => succeeded(Value!),
            PaymobCashInOutcome.Rejected => rejected(Error!),
            _ => unknown(Error!),
        };
    }

    /// <summary>Projects the value of a succeeded result and keeps any other outcome and its error.</summary>
    /// <typeparam name="TOut">The projected value type.</typeparam>
    /// <param name="mapper">The projection applied to the value of a succeeded result.</param>
    /// <returns>A result with the same outcome and error. Only a succeeded result keeps a value.</returns>
    public PaymobCashInResult<TOut> Map<TOut>(Func<T, TOut> mapper)
    {
        Argument.IsNotNull(mapper);

        return IsSucceeded
            ? new PaymobCashInResult<TOut>(Outcome, mapper(Value), error: null)
            : new PaymobCashInResult<TOut>(Outcome, value: default, Error);
    }
}

/// <summary>Creates <see cref="PaymobCashInResult{T}"/> values, for example to fake the broker in a test.</summary>
[PublicAPI]
public static class PaymobCashInResult
{
    /// <summary>Creates a result for a request Paymob accepted.</summary>
    /// <typeparam name="T">The parsed Paymob response type.</typeparam>
    /// <param name="value">The parsed Paymob response.</param>
    /// <returns>A succeeded result.</returns>
    public static PaymobCashInResult<T> Succeeded<T>(T value)
    {
        Argument.IsNotNull(value);

        return new(PaymobCashInOutcome.Succeeded, value, error: null);
    }

    /// <summary>Creates a result for a request Paymob definitely did not apply.</summary>
    /// <typeparam name="T">The parsed Paymob response type.</typeparam>
    /// <param name="error">Why the request was rejected.</param>
    /// <param name="value">The parsed rejection Paymob answered with, if any.</param>
    /// <returns>A rejected result.</returns>
    public static PaymobCashInResult<T> Rejected<T>(PaymobCashInResultError error, T? value = default)
    {
        Argument.IsNotNull(error);

        return new(PaymobCashInOutcome.Rejected, value, error);
    }

    /// <summary>Creates a result for a request that may have been applied.</summary>
    /// <typeparam name="T">The parsed Paymob response type.</typeparam>
    /// <param name="error">Why the outcome is unknown.</param>
    /// <param name="value">The parsed response Paymob answered with, if any.</param>
    /// <returns>An unknown result.</returns>
    public static PaymobCashInResult<T> Unknown<T>(PaymobCashInResultError error, T? value = default)
    {
        Argument.IsNotNull(error);

        return new(PaymobCashInOutcome.Unknown, value, error);
    }
}
