// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

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

    private static readonly JsonSerializerOptions _FaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes a fault body. It is never empty, so every transport carries it.</summary>
    public static byte[] WriteFault(ReplyFault fault)
    {
        return JsonSerializer.SerializeToUtf8Bytes(fault, _FaultJsonOptions);
    }

    /// <summary>
    /// Reads a fault body, or returns <see langword="null"/> when the body is not a readable fault with a code.
    /// </summary>
    public static ReplyFault? ReadFault(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return null;
        }

        try
        {
            var fault = JsonSerializer.Deserialize<ReplyFault>(body.Span, _FaultJsonOptions);
            return string.IsNullOrWhiteSpace(fault?.Code) ? null : fault;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The body of a fault reply.</summary>
/// <param name="Code">The stable fault code, usually one of <see cref="RequestFaultCodes"/>.</param>
/// <param name="ExceptionType">The responder's exception type name, present only when the responder host opts in.</param>
/// <param name="Detail">The responder's failure description, present only when the responder host opts in.</param>
internal sealed record ReplyFault(string Code, string? ExceptionType = null, string? Detail = null);
