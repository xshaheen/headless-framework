// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.RequestReply;

/// <summary>
/// The protocol values a request carries, passed to the publish path as typed inputs rather than custom headers: the
/// publish request factory rejects the request headers as custom headers, so neither the caller nor publish middleware
/// can forge or overwrite them.
/// </summary>
/// <param name="RequestId">The framework-owned identifier a reply names in <see cref="Headers.InReplyTo"/>.</param>
/// <param name="ReplyTo">The address of the calling process's reply listener.</param>
/// <param name="Deadline">The instant after which the responder neither starts the work nor replies.</param>
/// <param name="OnPrepared">
/// Receives the final envelope's tenant after publish middleware ran and before the request leaves the process, so the
/// pending call knows the tenant a reply must carry before any reply can arrive.
/// </param>
internal sealed record RequestStamp(
    string RequestId,
    string ReplyTo,
    DateTimeOffset Deadline,
    Action<string?> OnPrepared
);
