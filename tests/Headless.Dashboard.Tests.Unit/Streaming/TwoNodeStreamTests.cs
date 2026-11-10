// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Streaming;

/// <summary>
/// Two nodes sharing one module store and nothing else: a change made through one node reaches a stream on the other
/// through that node's own read of the store, with no broker between them.
/// </summary>
public sealed class TwoNodeStreamTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeChangeStore _store;

    public TwoNodeStreamTests()
    {
        _store = new FakeChangeStore(_time);
    }

    [Fact]
    public async Task should_show_a_row_failed_through_node_b_in_a_stream_on_node_a_within_the_poll_interval_plus_one_second()
    {
        await using var nodeA = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        await using var nodeB = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var clientA = nodeA.GetTestClient();
        await using var stream = await SseStream.OpenAsync(clientA, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);
        await FakeClock.WithinAsync(_store.Started(1), AbortToken);

        // Node B's module fails the row in the shared store and publishes it to node B's own streams, as a real module
        // does; nothing carries the publish to node A.
        var failedAt = _time.GetUtcNow();
        var failed = _store.Write("diag", "run", "r-1", projectionJson: """{"status":"Failed"}""");
        nodeB.Services.GetRequiredService<IDashboardEventPublisher>().Publish(failed);

        var change = await FakeClock.AdvanceUntilAsync(_time, stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        var latency = _time.GetUtcNow() - failedAt;

        change.Json.GetProperty("id").GetString().Should().Be("r-1");
        change.Json.GetProperty("projection").GetProperty("status").GetString().Should().Be("Failed");
        latency.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(2) + TimeSpan.FromSeconds(1));
    }
}
