// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Module;

/// <summary>
/// The Messaging module registered with <c>AddMessaging()</c> on a real host: it serves beside the Jobs module, fails
/// a host without Headless Messaging at start naming the missing call, and answers counts, lookups, and its own API.
/// </summary>
public sealed class MessagingModuleHostTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_serve_the_jobs_and_messaging_modules_under_one_base_path()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken, addJobs: true);
        using var client = app.GetTestClient();

        // when
        var messaging = await MessagingModuleTestHost.GetJsonAsync(
            client,
            "/headless/api/counts?product=messaging",
            AbortToken
        );
        var jobs = await MessagingModuleTestHost.GetJsonAsync(client, "/headless/api/counts?product=jobs", AbortToken);
        var nodes = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/nodes",
            AbortToken
        );

        // then
        messaging.GetProperty("product").GetString().Should().Be("messaging");
        messaging.GetProperty("counts").TryGetProperty(MessagingCounts.ReceivedFailed, out _).Should().BeTrue();
        jobs.GetProperty("product").GetString().Should().Be("jobs");
        jobs.GetProperty("counts").TryGetProperty("Failed", out _).Should().BeTrue();
        var node = nodes.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        node.GetProperty("name").GetString().Should().Be("node-a");
        node.GetProperty("stores").EnumerateArray().Select(static store => store.GetString()).Should().Equal("primary");
    }

    [Fact]
    public async Task should_fail_at_startup_naming_add_headless_messaging_when_messaging_is_not_registered()
    {
        // when
        var start = () => MessagingModuleTestHost.StartAsync(_time, AbortToken, addMessaging: false);

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("AddHeadlessMessaging");
    }

    [Fact]
    public async Task should_answer_every_sidebar_count_from_the_store_in_one_request()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        await MessagingRows.ReceivedFailureAsync(storage, "m-1", cancellationToken: AbortToken);
        await MessagingRows.ReceivedFailureAsync(storage, "m-2", cancellationToken: AbortToken);
        await MessagingRows.PublishedAsync(storage, "p-1", StatusName.Succeeded, AbortToken);
        await MessagingRows.PublishedAsync(storage, "p-2", StatusName.Scheduled, AbortToken);
        using var client = app.GetTestClient();

        // when
        var body = await MessagingModuleTestHost.GetJsonAsync(
            client,
            "/headless/api/counts?product=messaging",
            AbortToken
        );

        // then
        var counts = body.GetProperty("counts");
        counts.GetProperty("received.failed").GetProperty("count").GetInt64().Should().Be(2);
        counts.GetProperty("received.all").GetProperty("count").GetInt64().Should().Be(2);
        counts.GetProperty("received.retrying").GetProperty("count").GetInt64().Should().Be(0);
        counts.GetProperty("published.succeeded").GetProperty("count").GetInt64().Should().Be(1);
        counts.GetProperty("published.scheduled").GetProperty("count").GetInt64().Should().Be(1);
        counts.GetProperty("published.all").GetProperty("count").GetInt64().Should().Be(2);
        counts.GetProperty("published.failed").GetProperty("count").GetInt64().Should().Be(0);
        body.GetProperty("stale").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task should_find_a_received_row_by_its_storage_id_and_by_its_message_id()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingRows.ReceivedFailureAsync(storage, "order-7", cancellationToken: AbortToken);
        await MessagingRows.ReceivedFailureAsync(storage, "order-7 ", cancellationToken: AbortToken);
        using var client = app.GetTestClient();
        var expected = $"node-a~primary~{row.StorageId:D}";

        // when
        var byStorageId = await MessagingModuleTestHost.GetJsonAsync(
            client,
            $"/headless/api/resolve?q={row.StorageId}",
            AbortToken
        );
        var byMessageId = await MessagingModuleTestHost.GetJsonAsync(
            client,
            "/headless/api/resolve?q=order-7",
            AbortToken
        );

        // then: the message id with a trailing space is another message
        _Ids(byStorageId).Should().Equal(expected);
        _Ids(byMessageId).Should().Equal(expected);
    }

    [Fact]
    public async Task should_read_a_message_detail_by_its_object_id()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingRows.ReceivedFailureAsync(storage, "order-9", cancellationToken: AbortToken);
        using var client = app.GetTestClient();

        // when
        var detail = await MessagingModuleTestHost.GetJsonAsync(
            client,
            $"{MessagingModuleTestHost.Api}/messages/received/node-a~primary~{row.StorageId:D}",
            AbortToken
        );
        using var otherNode = await client.GetAsync(
            new Uri(
                $"{MessagingModuleTestHost.Api}/messages/received/node-b~primary~{row.StorageId:D}",
                UriKind.Relative
            ),
            AbortToken
        );

        // then
        detail.GetProperty("row").GetProperty("messageId").GetString().Should().Be("order-9");
        detail.GetProperty("row").GetProperty("status").GetString().Should().Be("Failed");
        detail.GetProperty("row").GetProperty("consumer").GetString().Should().Be(MessagingRows.Consumer);
        otherNode.StatusCode.Should().Be(HttpStatusCode.NotFound, "this node answers only for its own ids");
    }

    [Fact]
    public async Task should_list_scheduled_deliveries_for_a_viewer_without_a_named_operator()
    {
        // given: no authentication signs every viewer in as the placeholder "anonymous", which may not operate
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        using var client = app.GetTestClient();

        // when
        var scheduled = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/scheduled",
            AbortToken
        );

        // then
        scheduled.GetProperty("supported").GetBoolean().Should().BeTrue();
        scheduled.GetProperty("store").GetString().Should().Be("primary");
    }

    [Fact]
    public async Task should_report_no_value_for_the_seconds_before_the_listener_started()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var started = Stopwatch.StartNew();
        using var client = app.GetTestClient();

        // when
        var metrics = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/metrics/realtime",
            AbortToken
        );

        // then: only the newest seconds since the host started can hold a measured value
        var measurable = (int)Math.Ceiling(started.Elapsed.TotalSeconds) + 2;
        var published = metrics.GetProperty("publishedPerSecond").EnumerateArray().ToArray();
        var consumed = metrics.GetProperty("consumedPerSecond").EnumerateArray().ToArray();
        metrics.GetProperty("seconds").GetArrayLength().Should().Be(MessagingMetricsEventListener.HistorySize);
        published.Should().HaveCount(MessagingMetricsEventListener.HistorySize);
        published[..^measurable].Should().AllSatisfy(value => value.ValueKind.Should().Be(JsonValueKind.Null));
        consumed[..^measurable].Should().AllSatisfy(value => value.ValueKind.Should().Be(JsonValueKind.Null));
    }

    private static IEnumerable<string?> _Ids(JsonElement resolved) =>
        resolved
            .GetProperty("modules")
            .EnumerateArray()
            .Where(static module =>
                string.Equals(module.GetProperty("id").GetString(), "messaging", StringComparison.Ordinal)
            )
            .SelectMany(static module => module.GetProperty("items").EnumerateArray())
            .Select(static item => item.GetProperty("target").GetProperty("id").GetString());
}
