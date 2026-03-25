// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NATS.Client.Core;

namespace Tests;

[Collection("Nats")]
public sealed class NatsConnectionTest(NatsFixture fixture)
{
    [Fact]
    public async Task should_connect_to_nats_via_fixture()
    {
        var connStr = fixture.ConnectionString;

        // NATS.Net v2 ConnectAsync starts connection but may not complete
        // synchronously. PingAsync forces a roundtrip that blocks until
        // the connection is fully established.
        var opts = NatsOpts.Default with
        {
            Url = connStr,
        };
        await using var conn = new NatsConnection(opts);
        var rtt = await conn.PingAsync();

        rtt.TotalMilliseconds.Should().BeGreaterThan(0);
    }
}
