// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard;
using Headless.DevInbox.Dashboard.Internal;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Clear as an Operation: it removes only what existed at the confirm, however many captures arrive later.</summary>
public sealed class ClearOperationTests : TestBase
{
    private const string _ClearSms = DevInboxTestHost.Commands + "/dev-inbox.sms.clear";

    private static readonly JsonElement _WholeChannel = JsonSerializer.SerializeToElement(
        new { search = (string?)null }
    );

    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_count_and_sample_the_scope_require_a_reason_and_clear_every_confirmed_sms()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken, log: _log);
        using var client = DevInboxTestHost.Client(app);
        await DevInboxTestHost.SendSmsAsync(app, "one", AbortToken);
        await DevInboxTestHost.SendSmsAsync(app, "two", AbortToken);
        await DevInboxTestHost.SendSmsAsync(app, "three", AbortToken);

        // when
        using var withoutReason = await DevInboxTestHost.PostAsync(
            client,
            _ClearSms + "/confirm",
            new { filter = new { search = (string?)null } },
            AbortToken
        );
        using var confirm = await DevInboxTestHost.PostAsync(
            client,
            _ClearSms + "/confirm",
            new { filter = new { search = (string?)null }, reason = "start over" },
            AbortToken
        );
        var scope = await DevInboxTestHost.ReadAsync(confirm, AbortToken);
        using var chunk = await DevInboxTestHost.PostAsync(
            client,
            _ClearSms + "/chunks",
            new
            {
                token = scope.GetProperty("token").GetString(),
                cursor = (string?)null,
                confirmed = true,
            },
            AbortToken
        );

        // then
        withoutReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        scope.GetProperty("count").GetInt64().Should().Be(3);
        scope.GetProperty("sample").GetArrayLength().Should().Be(3);
        chunk.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await DevInboxTestHost.ReadAsync(chunk, AbortToken);
        result
            .GetProperty("rows")
            .EnumerateArray()
            .Should()
            .OnlyContain(row => row.GetProperty("outcome").GetString() == "done");
        result.GetProperty("rows").GetArrayLength().Should().Be(3);
        DevInboxTestHost.SmsStore(app).Count.Should().Be(0);
        _log.Entries.Select(static entry => entry.Action)
            .Should()
            .Equal(OperatorLogAction.OperationConfirmed, OperatorLogAction.OperationChunk);
    }

    [Fact]
    public async Task should_keep_an_sms_captured_after_the_confirm()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        var module = _Module(app);
        await DevInboxTestHost.SendSmsAsync(app, "before", AbortToken);
        var scope = await module.PrepareAsync(_Scope(), AbortToken);
        await DevInboxTestHost.SendSmsAsync(app, "after", AbortToken);

        // when
        var chunk = await module.RunChunkAsync(_Chunk(scope.HighWaterMark, cursor: null, chunkSize: 500), AbortToken);

        // then
        chunk.Rows.Should().ContainSingle();
        chunk.NextCursor.Should().BeNull();
        DevInboxTestHost.SmsStore(app).Query(new()).Select(static sms => sms.Text).Should().Equal("after");
    }

    [Fact]
    public async Task should_clear_every_confirmed_sms_when_more_later_ones_arrive_than_one_chunk_holds()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        var module = _Module(app);

        for (var i = 0; i < 5; i++)
        {
            await DevInboxTestHost.SendSmsAsync(app, $"before {i}", AbortToken);
        }

        var scope = await module.PrepareAsync(_Scope(), AbortToken);

        for (var i = 0; i < 5; i++)
        {
            await DevInboxTestHost.SendSmsAsync(app, $"after {i}", AbortToken);
        }

        // when: chunks of two walk the scope until it reports no next cursor
        string? cursor = null;
        var chunks = 0;

        do
        {
            var chunk = await module.RunChunkAsync(_Chunk(scope.HighWaterMark, cursor, chunkSize: 2), AbortToken);
            cursor = chunk.NextCursor;
            chunks++;
        } while (cursor is not null && chunks < 10);

        // then
        scope.Count.Should().Be(5);
        chunks.Should().Be(3);
        DevInboxTestHost
            .SmsStore(app)
            .Query(new())
            .Select(static sms => sms.Text)
            .Should()
            .BeEquivalentTo("after 0", "after 1", "after 2", "after 3", "after 4");
    }

    [Fact]
    public async Task should_refuse_a_mark_from_another_store_instance_and_delete_nothing()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        var module = _Module(app);
        await DevInboxTestHost.SendSmsAsync(app, "kept", AbortToken);
        var scope = await module.PrepareAsync(_Scope(), AbortToken);
        var version = scope.HighWaterMark[(scope.HighWaterMark.IndexOf(':', StringComparison.Ordinal) + 1)..];

        // when
        var run = () =>
            module.RunChunkAsync(_Chunk("another-process:" + version, cursor: null, chunkSize: 500), AbortToken);

        // then
        await run.Should().ThrowAsync<ArgumentException>().WithMessage("*restarted*");
        DevInboxTestHost.SmsStore(app).Count.Should().Be(1);
    }

    [Fact]
    public async Task should_report_nothing_left_when_a_finished_clear_runs_again()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        var module = _Module(app);
        await DevInboxTestHost.SendSmsAsync(app, "one", AbortToken);
        var scope = await module.PrepareAsync(_Scope(), AbortToken);
        await module.RunChunkAsync(_Chunk(scope.HighWaterMark, cursor: null, chunkSize: 500), AbortToken);

        // when
        var again = await module.RunChunkAsync(_Chunk(scope.HighWaterMark, cursor: null, chunkSize: 500), AbortToken);

        // then
        again.Rows.Should().BeEmpty();
        again.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task should_publish_one_removal_per_cleared_sms()
    {
        // given
        var published = new RecordingEventPublisher();
        await using var app = await DevInboxTestHost.StartAsync(
            AbortToken,
            configure: services => services.AddSingleton<IDashboardEventPublisher>(published)
        );
        var module = _Module(app);
        var first = await DevInboxTestHost.SendSmsAsync(app, "one", AbortToken);
        var second = await DevInboxTestHost.SendSmsAsync(app, "two", AbortToken);
        var scope = await module.PrepareAsync(_Scope(), AbortToken);

        // when
        await module.RunChunkAsync(_Chunk(scope.HighWaterMark, cursor: null, chunkSize: 500), AbortToken);

        // then
        published
            .Changes.Select(static change => (change.Target.Kind, change.Target.Id, change.Projection is null))
            .Should()
            .BeEquivalentTo([("sms", first.Id.ToString("D"), true), ("sms", second.Id.ToString("D"), true)]);
    }

    [Fact]
    public async Task should_clear_only_the_sms_matching_the_search_and_log_the_search_redacted()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        var module = _Module(app);
        await DevInboxTestHost.SendSmsAsync(app, "your code is 1234", AbortToken);
        await DevInboxTestHost.SendSmsAsync(app, "another CODE 5678", AbortToken);
        await DevInboxTestHost.SendSmsAsync(app, "hello", AbortToken);
        var filter = JsonSerializer.SerializeToElement(new { search = "code" });

        // when
        var scope = await module.PrepareAsync(_Scope(filter), AbortToken);
        var chunk = await module.RunChunkAsync(
            _Chunk(scope.HighWaterMark, cursor: null, chunkSize: 500, filter),
            AbortToken
        );

        // then
        scope.Count.Should().Be(2);
        var logged = scope.LoggedFilter!.Value.GetRawText();
        logged.Should().Contain(DevInboxCommands.RedactedSearch).And.NotContain("code");
        chunk.Rows.Should().HaveCount(2);
        DevInboxTestHost.SmsStore(app).Query(new()).Select(static sms => sms.Text).Should().Equal("hello");
    }

    [Theory]
    [InlineData("""{"query":"code"}""")]
    [InlineData("""{"search":"a","search":"b"}""")]
    [InlineData("""{"search":5}""")]
    [InlineData("""[]""")]
    public async Task should_refuse_a_filter_that_is_not_a_single_search_and_delete_nothing(string json)
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        var module = _Module(app);
        await DevInboxTestHost.SendSmsAsync(app, "kept", AbortToken);
        using var document = JsonDocument.Parse(json);

        // when
        var act = () => module.PrepareAsync(_Scope(document.RootElement.Clone()), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        DevInboxTestHost.SmsStore(app).Count.Should().Be(1);
    }

    private static IDashboardOperationRunner _Module(WebApplication app) =>
        app.Services.GetRequiredService<DevInboxModule>();

    private static DashboardOperationScopeRequest _Scope(JsonElement? filter = null) =>
        new()
        {
            CommandId = "dev-inbox.sms.clear",
            Filter = filter ?? _WholeChannel,
            CountLimit = 100_000,
            SampleSize = 5,
            User = new ClaimsPrincipal(),
        };

    private static DashboardOperationChunkRequest _Chunk(
        string mark,
        string? cursor,
        int chunkSize,
        JsonElement? filter = null
    ) =>
        new()
        {
            CommandId = "dev-inbox.sms.clear",
            Filter = filter ?? _WholeChannel,
            HighWaterMark = mark,
            Cursor = cursor,
            ChunkSize = chunkSize,
            User = new ClaimsPrincipal(),
        };
}
