// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>Thrown when the responder answers a request with a fault reply.</summary>
/// <remarks>
/// <see cref="RemoteExceptionType"/> and <see cref="Detail"/> cross the process boundary only when the responder host
/// opts in, so by default a caller learns only the stable <see cref="Code"/>.
/// </remarks>
/// <param name="requestId">The request's identifier.</param>
/// <param name="code">The fault code, usually one of <see cref="RequestFaultCodes"/>.</param>
/// <param name="remoteExceptionType">The responder's exception type name, when the responder shared it.</param>
/// <param name="detail">The responder's failure description, when the responder shared it.</param>
/// <exception cref="ArgumentException"><paramref name="code"/> is empty or whitespace.</exception>
/// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
[PublicAPI]
public sealed class RequestFaultedException(
    string? requestId,
    string code,
    string? remoteExceptionType = null,
    string? detail = null
) : RequestReplyException(_Describe(requestId, code, remoteExceptionType, detail), requestId)
{
    /// <summary>Gets the fault code, usually one of <see cref="RequestFaultCodes"/>.</summary>
    public string Code { get; } = code;

    /// <summary>
    /// Gets the responder's exception type name, or <see langword="null"/> when the responder host did not share it.
    /// </summary>
    public string? RemoteExceptionType { get; } = remoteExceptionType;

    /// <summary>
    /// Gets the responder's failure description, or <see langword="null"/> when the responder host did not share it.
    /// </summary>
    public string? Detail { get; } = detail;

    private static string _Describe(string? requestId, string code, string? remoteExceptionType, string? detail)
    {
        Argument.IsNotNullOrWhiteSpace(code);

        var message = $"The responder faulted request '{requestId}' with code '{code}'.";

        if (remoteExceptionType is not null)
        {
            message += $" Remote exception: {remoteExceptionType}.";
        }

        if (detail is not null)
        {
            message += $" Detail: {detail}";
        }

        return message;
    }
}
