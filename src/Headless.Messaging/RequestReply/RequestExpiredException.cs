// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.RequestReply;

/// <summary>
/// Ends a request whose caller stopped waiting before the attempt started. It is recorded on the failed row so the
/// dashboard shows why the consumer never ran.
/// </summary>
#pragma warning disable CA1064 // Deliberately internal: the executor maps it to a fault code; no caller catches it.
internal sealed class RequestExpiredException(DateTimeOffset? deadline)
    : Exception(
        $"request_expired: the request's deadline {deadline?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)} "
            + "passed before this attempt started, so the consumer did not run and no reply was sent."
    );
#pragma warning restore CA1064
