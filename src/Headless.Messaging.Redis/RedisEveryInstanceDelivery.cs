// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// The settlement token of a group-less read. CommitAsync and RejectAsync ignore it: the read left no pending entry to
/// acknowledge, and an every-instance delivery is never redelivered.
/// </summary>
internal readonly record struct RedisEveryInstanceDelivery(string Stream, string Id);
