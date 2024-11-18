// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Primitives;

namespace Framework.Payments.Paymob.Services.CashOut.Responses;

public sealed class CashOutResult<T>
{
    private CashOutResult() { }

    /// <summary>Flag indicating whether if the operation succeeded or not.</summary>
    /// <value>True if the operation succeeded, otherwise false.</value>
    [MemberNotNullWhen(true, nameof(Data), nameof(Response))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded { get; private set; }

    /// <summary>Success data. Not null when success otherwise will be.</summary>
    public T? Data { get; private set; }

    /// <summary>Error message. Not null when failed otherwise will be.</summary>
    public ErrorDescriptor? Error { get; private set; }

    public string? Response { get; private set; }

    public static CashOutResult<T> Success(T data, string response)
    {
        return new()
        {
            Succeeded = true,
            Data = data,
            Response = response,
        };
    }

    public static CashOutResult<T> Failure(ErrorDescriptor error, string? response)
    {
        return new()
        {
            Succeeded = false,
            Error = error,
            Response = response,
        };
    }
}
