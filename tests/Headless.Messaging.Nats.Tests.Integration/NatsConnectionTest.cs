// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NATS.Client.Core;

namespace Tests;

[Collection("Nats")]
public sealed class NatsConnectionTest(NatsFixture fixture)
{
    [Fact]
    public async Task should_connect_and_ping_nats()
    {
        var opts = NatsOpts.Default with
        {
            Url = fixture.ConnectionString,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            RetryOnInitialConnect = true,
        };
        await using var conn = new NatsConnection(opts);
        var rtt = await conn.PingAsync();

        rtt.TotalMilliseconds.Should().BeGreaterThan(0);
    }
}
