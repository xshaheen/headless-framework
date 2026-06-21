// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Payments.Paymob.Services.CashOut.Responses;

/// <summary>
/// The result of a CashOut disbursement operation, encapsulating either success data or an error
/// descriptor without throwing an exception for Paymob-level failures.
/// </summary>
/// <typeparam name="T">
/// The success payload type (<c>CashOutResponse</c> for most channels,
/// <c>KioskCashOutResponse</c> for Aman kiosk disbursements).
/// </typeparam>
public sealed class CashOutResult<T>
{
    internal CashOutResult() { }

    /// <summary>
    /// Indicates whether the disbursement succeeded (or is pending acceptance by the provider).
    /// </summary>
    /// <value><see langword="true"/> when <c>Data</c> and <c>Response</c> are populated; <see langword="false"/> when <c>Error</c> is populated.</value>
    [MemberNotNullWhen(true, nameof(Data), nameof(Response))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded { get; init; }

    /// <summary>
    /// The disbursement outcome data. Non-null when <c>Succeeded</c> is <see langword="true"/>.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// A structured error descriptor. Non-null when <c>Succeeded</c> is <see langword="false"/>.
    /// </summary>
    public ErrorDescriptor? Error { get; init; }

    /// <summary>
    /// The raw JSON response body returned by Paymob, provided for logging and diagnostics.
    /// Non-null when <c>Succeeded</c> is <see langword="true"/>; may be <see langword="null"/> on
    /// transport-level failures where no body was received.
    /// </summary>
    public string? Response { get; init; }
}

/// <summary>
/// Factory methods for creating <c>CashOutResult</c> instances.
/// </summary>
public static class CashOutResult
{
    /// <summary>Creates a successful result.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="data">The success payload.</param>
    /// <param name="response">The raw Paymob API response body.</param>
    public static CashOutResult<T> Success<T>(T data, string response)
    {
        return new()
        {
            Succeeded = true,
            Data = data,
            Response = response,
        };
    }

    /// <summary>Creates a failure result.</summary>
    /// <typeparam name="T">The success payload type.</typeparam>
    /// <param name="error">A structured error descriptor describing the failure.</param>
    /// <param name="response">The raw Paymob API response body, if available.</param>
    public static CashOutResult<T> Failure<T>(ErrorDescriptor error, string? response)
    {
        return new()
        {
            Succeeded = false,
            Error = error,
            Response = response,
        };
    }
}
