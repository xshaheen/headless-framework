// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.CashOut;

/// <summary>The outcome of one Paymob CashOut operation.</summary>
/// <typeparam name="T">The parsed Paymob response type.</typeparam>
/// <remarks>
/// <para>
/// Branch on <see cref="Outcome"/> or call <see cref="Match{TResult}"/>, which makes you handle all three
/// outcomes. There is no single "failed" flag, so code cannot treat an <see cref="PaymobCashOutOutcome.Unknown"/>
/// payout as a rejected one by accident.
/// </para>
/// <para>
/// Never send a money-moving request again after <see cref="PaymobCashOutOutcome.Unknown"/> until the payout
/// callback or <c>IPaymobCashOutBroker.GetTransactionsAsync</c> shows that the first one did not apply.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PaymobCashOutResult<T>
{
    internal PaymobCashOutResult(PaymobCashOutOutcome outcome, T? value, PaymobCashOutResultError? error)
    {
        Outcome = outcome;
        Value = value;
        Error = error;
        IsSucceeded = outcome is PaymobCashOutOutcome.Succeeded;
        IsRejected = outcome is PaymobCashOutOutcome.Rejected;
        IsUnknown = outcome is PaymobCashOutOutcome.Unknown;
    }

    /// <summary>Gets what Paymob did with the request.</summary>
    public PaymobCashOutOutcome Outcome { get; }

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
    /// and also set on another outcome when Paymob answered with a readable body, such as a failed disbursement.
    /// </summary>
    public T? Value { get; }

    /// <summary>Gets why the outcome is rejected or unknown. Set whenever <see cref="IsSucceeded"/> is <see langword="false"/>.</summary>
    public PaymobCashOutResultError? Error { get; }

    /// <summary>Invokes the function for the outcome and returns its result.</summary>
    /// <typeparam name="TResult">The type both branches produce.</typeparam>
    /// <param name="succeeded">Invoked with the parsed response when Paymob accepted the request.</param>
    /// <param name="rejected">Invoked with the error when Paymob definitely did not apply the request.</param>
    /// <param name="unknown">Invoked with the error when the request may have been applied.</param>
    /// <returns>The value the invoked function returned.</returns>
    public TResult Match<TResult>(
        Func<T, TResult> succeeded,
        Func<PaymobCashOutResultError, TResult> rejected,
        Func<PaymobCashOutResultError, TResult> unknown
    )
    {
        Argument.IsNotNull(succeeded);
        Argument.IsNotNull(rejected);
        Argument.IsNotNull(unknown);

        return Outcome switch
        {
            PaymobCashOutOutcome.Succeeded => succeeded(Value!),
            PaymobCashOutOutcome.Rejected => rejected(Error!),
            _ => unknown(Error!),
        };
    }

    /// <summary>Projects the value of a succeeded result and keeps any other outcome and its error.</summary>
    /// <typeparam name="TOut">The projected value type.</typeparam>
    /// <param name="mapper">The projection applied to the value of a succeeded result.</param>
    /// <returns>A result with the same outcome and error. Only a succeeded result keeps a value.</returns>
    public PaymobCashOutResult<TOut> Map<TOut>(Func<T, TOut> mapper)
    {
        Argument.IsNotNull(mapper);

        return IsSucceeded
            ? new PaymobCashOutResult<TOut>(Outcome, mapper(Value), error: null)
            : new PaymobCashOutResult<TOut>(Outcome, value: default, Error);
    }
}

/// <summary>Creates <see cref="PaymobCashOutResult{T}"/> values, for example to fake the broker in a test.</summary>
[PublicAPI]
public static class PaymobCashOutResult
{
    /// <summary>Creates a result for a request Paymob accepted.</summary>
    /// <typeparam name="T">The parsed Paymob response type.</typeparam>
    /// <param name="value">The parsed Paymob response.</param>
    /// <returns>A succeeded result.</returns>
    public static PaymobCashOutResult<T> Succeeded<T>(T value)
    {
        Argument.IsNotNull(value);

        return new(PaymobCashOutOutcome.Succeeded, value, error: null);
    }

    /// <summary>Creates a result for a request Paymob definitely did not apply.</summary>
    /// <typeparam name="T">The parsed Paymob response type.</typeparam>
    /// <param name="error">Why the request was rejected.</param>
    /// <param name="value">The parsed rejection Paymob answered with, if any.</param>
    /// <returns>A rejected result.</returns>
    public static PaymobCashOutResult<T> Rejected<T>(PaymobCashOutResultError error, T? value = default)
    {
        Argument.IsNotNull(error);

        return new(PaymobCashOutOutcome.Rejected, value, error);
    }

    /// <summary>Creates a result for a request that may have been applied.</summary>
    /// <typeparam name="T">The parsed Paymob response type.</typeparam>
    /// <param name="error">Why the outcome is unknown.</param>
    /// <param name="value">The parsed response Paymob answered with, if any.</param>
    /// <returns>An unknown result.</returns>
    public static PaymobCashOutResult<T> Unknown<T>(PaymobCashOutResultError error, T? value = default)
    {
        Argument.IsNotNull(error);

        return new(PaymobCashOutOutcome.Unknown, value, error);
    }
}
