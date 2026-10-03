// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed record RedisReplyPayload(IDictionary<string, string?>? Headers, byte[]? Body);
