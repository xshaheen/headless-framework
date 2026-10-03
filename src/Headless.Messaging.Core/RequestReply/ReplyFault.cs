// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;

namespace Headless.Messaging.RequestReply;

/// <summary>The body of a fault reply.</summary>
/// <param name="Code">The stable fault code, usually one of <see cref="RequestFaultCodes"/>.</param>
/// <param name="ExceptionType">The responder's exception type name, present only when the responder host opts in.</param>
/// <param name="Detail">The responder's failure description, present only when the responder host opts in.</param>
internal sealed record ReplyFault(string Code, string? ExceptionType = null, string? Detail = null);
