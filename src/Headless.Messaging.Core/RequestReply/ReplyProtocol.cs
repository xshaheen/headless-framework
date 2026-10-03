// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;

namespace Headless.Messaging.RequestReply;

/// <summary>
/// The reply envelope both sides of a request agree on: the <see cref="Headers.ReplyStatus"/> values and the fault body.
/// </summary>
/// <remarks>
/// The fault body is framework-owned, so it is written with fixed JSON settings rather than the host serializer: two
/// hosts with different serializer settings, or a custom serializer, still read each other's faults.
/// </remarks>
internal static class ReplyProtocol
{
    /// <summary>The <see cref="Headers.ReplyStatus"/> of a reply whose body is the response.</summary>
    public const string StatusOk = "ok";

    /// <summary>The <see cref="Headers.ReplyStatus"/> of a reply whose body is a <see cref="ReplyFault"/>.</summary>
    public const string StatusFault = "fault";

    /// <summary>The longest fault detail either side keeps: a hint for the caller, not a stack dump.</summary>
    public const int MaxFaultDetailLength = 1024;

    // A fault code is a short stable value and an exception type a type name; anything longer is not one of them.
    private const int _MaxFaultCodeLength = 64;
    private const int _MaxFaultExceptionTypeLength = 256;

    private static readonly JsonSerializerOptions _FaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Makes a request id safe to log. The id is untrusted wire data, so it is bounded by the same limit as a reply
    /// address.
    /// </summary>
    public static string? SanitizeRequestId(string? requestId)
    {
        return LogSanitizer.Sanitize(requestId, ReplyAddresses.MaxLength);
    }

    /// <summary>Serializes a fault body. It is never empty, so every transport carries it.</summary>
    public static byte[] WriteFault(ReplyFault fault)
    {
        return JsonSerializer.SerializeToUtf8Bytes(fault, _FaultJsonOptions);
    }

    /// <summary>
    /// Reads a fault body, or returns <see langword="null"/> when the body is not a readable fault with a code.
    /// </summary>
    /// <remarks>
    /// The body is untrusted wire data: anyone able to publish to a live reply address can write one, and its fields end
    /// up in the caller's exception message, which is commonly logged. Each field is stripped of the characters that
    /// forge or split log lines and bounded in length.
    /// </remarks>
    public static ReplyFault? ReadFault(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return null;
        }

        ReplyFault? fault;
        try
        {
            fault = JsonSerializer.Deserialize<ReplyFault>(body.Span, _FaultJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        var code = LogSanitizer.Sanitize(fault?.Code, _MaxFaultCodeLength);
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return new ReplyFault(
            code,
            LogSanitizer.Sanitize(fault!.ExceptionType, _MaxFaultExceptionTypeLength),
            LogSanitizer.Sanitize(fault.Detail, MaxFaultDetailLength)
        );
    }
}
