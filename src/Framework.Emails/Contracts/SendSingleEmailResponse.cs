// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Emails.Contracts;

public sealed class SendSingleEmailResponse
{
    private SendSingleEmailResponse() { }

    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    public string? FailureError { get; private init; }

    public static SendSingleEmailResponse Succeeded()
    {
        return new() { Success = true };
    }

    public static SendSingleEmailResponse Failed(string failureError)
    {
        return new() { Success = false, FailureError = failureError };
    }
}
