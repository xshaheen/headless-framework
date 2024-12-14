// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Primitives;

namespace Framework.Payments.Paymob.Services.CashOut.Responses;

public sealed class CashOutResult<T>
{
    internal CashOutResult() { }

    /// <summary>Flag indicating whether if the operation succeeded or not.</summary>
    /// <value>True if the operation succeeded, otherwise false.</value>
    [MemberNotNullWhen(true, nameof(Data), nameof(Response))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded { get; init; }

    /// <summary>Success data. Not null when success otherwise will be.</summary>
    public T? Data { get; init; }

    /// <summary>Error message. Not null when failed otherwise will be.</summary>
    public ErrorDescriptor? Error { get; init; }

    public string? Response { get; init; }
}

public static class CashOutResult
{
    public static CashOutResult<T> Success<T>(T data, string response)
    {
        return new()
        {
            Succeeded = true,
            Data = data,
            Response = response,
        };
    }

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
