// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.RequestReply;

namespace Tests;

/// <summary>The shared request/reply suite against a real Redis server, with replies on pub/sub channels.</summary>
[Collection<RedisMessagingFixture>]
public sealed class RedisRequestReplyConformanceTests(RedisMessagingFixture fixture)
    : TransportRequestReplyConformanceTestsBase
{
    protected override ValueTask<TransportProviderConformanceDriver> CreateDriverAsync()
    {
        return ValueTask.FromResult<TransportProviderConformanceDriver>(new RedisProviderConformanceDriver(fixture));
    }
}
