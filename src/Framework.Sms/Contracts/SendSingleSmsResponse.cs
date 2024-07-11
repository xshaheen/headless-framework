using System.Diagnostics.CodeAnalysis;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Sms;

public sealed class SendSingleSmsResponse
{
    private SendSingleSmsResponse() { }

    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    public string? FailureError { get; private init; }

    public static SendSingleSmsResponse Succeeded()
    {
        return new() { Success = true };
    }

    public static SendSingleSmsResponse Failed(string failureError)
    {
        return new() { Success = false, FailureError = failureError };
    }
}
