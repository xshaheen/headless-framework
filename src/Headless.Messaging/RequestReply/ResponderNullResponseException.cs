// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.RequestReply;

/// <summary>
/// Fails a request whose responder returned <see langword="null"/>. A caller is promised a response or a typed failure,
/// never null, so the attempt ends terminally and the caller gets a <see cref="RequestFaultCodes.NullResponse"/> fault.
/// </summary>
#pragma warning disable CA1064 // Deliberately internal: the executor maps it to a fault code; no caller catches it.
internal sealed class ResponderNullResponseException(Type responderType)
    : Exception($"Responder {responderType.FullName ?? responderType.Name} returned null; a request needs a response.");
#pragma warning restore CA1064
