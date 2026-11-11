// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests.Module;

/// <summary>
/// The published and received lists' filters, sorts, and searches through the module's own API: a search's text
/// travels only in a POST body, a store that runs out of search time makes the answer incomplete rather than empty,
/// and a cursor is refused under another sort.
/// </summary>
public sealed class MessagingListSearchTests : TestBase
{
    private const string _Outbox = "billing";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_find_content_from_a_post_body_and_never_log_the_text_searched_for()
    {
        // given: two published rows, one holding the order number; every log line of the host is captured
        using var logs = new CapturingLoggerProvider();
        await using var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            configure: services =>
                services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
        );
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var order = await storage.StoreMessageAsync(
            "orders.placed",
            _Message(new { Order = "ORD-991" }),
            cancellationToken: AbortToken
        );
        await storage.StoreMessageAsync(
            "orders.placed",
            _Message(new { Order = "ORD-100" }),
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        // when
        using var response = await _SearchAsync(client, "published", new { content = "ord-991" });

        // then: one match, served as a list row without its content, and the text in neither the URL nor a log
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.RequestMessage!.RequestUri!.ToString().Should().NotContain("991");
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("storageId").GetGuid().Should().Be(order.StorageId);
        items[0].TryGetProperty("content", out _).Should().BeFalse();
        body.Should().NotContain("ORD-991", "a search serves rows, never the body it matched");
        document.RootElement.GetProperty("incomplete").GetBoolean().Should().BeFalse();
        logs.Lines.Should().Contain(line => line.Contains("/published/search", StringComparison.Ordinal));
        logs.Lines.Should().NotContain(line => line.Contains("991", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_find_a_correlation_id_exactly_and_skip_a_body_that_merely_contains_it()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var correlated = await storage.StoreMessageAsync(
            "orders.placed",
            _Message(new { Order = "ORD-1" }, correlationId: "c-42"),
            cancellationToken: AbortToken
        );
        await storage.StoreMessageAsync(
            "orders.placed",
            _Message(new { Order = "ORD-2" }, correlationId: "c-420"),
            cancellationToken: AbortToken
        );
        await storage.StoreMessageAsync(
            "orders.placed",
            _Message(new { Note = "c-42" }, correlationId: "c-7"),
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        // when
        using var response = await _SearchAsync(client, "published", new { correlationId = "c-42" });

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document
            .RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("storageId").GetGuid())
            .Should()
            .Equal(correlated.StorageId);
    }

    [Fact]
    public async Task should_answer_incomplete_and_not_unavailable_when_a_store_runs_out_of_search_time()
    {
        // given: an outbox whose reads never answer, beside the in-memory main store
        await using var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            configure: services =>
            {
                services.Configure<MessagingDashboardOptions>(options =>
                {
                    options.StoreTimeout = TimeSpan.FromMilliseconds(200);
                    options.SearchTimeout = TimeSpan.FromMilliseconds(300);
                });
                services.AddSingleton(provider =>
                    _Stores(provider.GetRequiredService<IDataStorage>(), _HangingOutbox())
                );
            }
        );
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var order = await storage.StoreMessageAsync(
            "orders.placed",
            _Message(new { Order = "ORD-991" }),
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        // when
        using var search = await _SearchAsync(client, "published", new { content = "ORD-991" });
        var list = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/published",
            AbortToken
        );

        // then: the search keeps what the main store found and says it is incomplete; a list names the store down
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await search.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("incomplete").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("unavailable").GetArrayLength().Should().Be(0);
        document
            .RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("storageId").GetGuid())
            .Should()
            .Equal(order.StorageId);
        document
            .RootElement.GetProperty("nextCursor")
            .GetString()
            .Should()
            .NotBeNull("the outbox is read again on the next page");
        list.GetProperty("incomplete").GetBoolean().Should().BeFalse();
        list.GetProperty("unavailable")
            .EnumerateArray()
            .Select(static store => store.GetString())
            .Should()
            .Equal(_Outbox);
    }

    [Fact]
    public async Task should_list_only_queue_rows_for_a_queue_lane_filter_and_name_a_failing_store()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            configure: services =>
                services.AddSingleton(provider =>
                    _Stores(provider.GetRequiredService<IDataStorage>(), _FailingOutbox())
                )
        );
        var storage = app.Services.GetRequiredService<IDataStorage>();
        await storage.StoreMessageAsync("orders.placed", _Medium(MessageLane.Bus), cancellationToken: AbortToken);
        var queued = await storage.StoreMessageAsync(
            "orders.placed",
            _Medium(MessageLane.Queue),
            cancellationToken: AbortToken
        );
        using var client = app.GetTestClient();

        // when
        var list = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/published?lane=Queue",
            AbortToken
        );

        // then
        list.GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("storageId").GetGuid())
            .Should()
            .Equal(queued.StorageId);
        list.GetProperty("items")[0].GetProperty("lane").GetString().Should().Be("Queue");
        list.GetProperty("unavailable")
            .EnumerateArray()
            .Select(static store => store.GetString())
            .Should()
            .Equal(_Outbox);
    }

    [Fact]
    public async Task should_page_a_name_sorted_list_and_refuse_its_cursor_under_the_added_sort()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        foreach (var name in new[] { "orders.c", "orders.a", "orders.b" })
        {
            await storage.StoreMessageAsync(name, _Message(new { Order = name }), cancellationToken: AbortToken);
        }

        using var client = app.GetTestClient();

        // when
        var first = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/published?sort=name&limit=2",
            AbortToken
        );
        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        var second = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + $"/published?sort=name&limit=2&after={cursor}",
            AbortToken
        );
        using var refused = await client.GetAsync(
            new Uri(MessagingModuleTestHost.Api + $"/published?limit=2&after={cursor}", UriKind.Relative),
            AbortToken
        );

        // then: A to Z across pages, and the added-sorted list refuses the name-sorted cursor
        first
            .GetProperty("items")
            .EnumerateArray()
            .Concat(second.GetProperty("items").EnumerateArray())
            .Select(static item => item.GetProperty("name").GetString())
            .Should()
            .Equal("orders.a", "orders.b", "orders.c");
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(AbortToken));
        problem.RootElement.GetProperty("rules").GetProperty("after").GetString().Should().Be("invalid");
    }

    [Fact]
    public async Task should_list_rows_added_in_a_range_with_an_exclusive_end()
    {
        // given: rows added an hour apart
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var added = new List<MediumMessage>();
        for (var i = 0; i < 3; i++)
        {
            added.Add(
                await storage.StoreMessageAsync(
                    "orders.placed",
                    _Message(new { Order = i }),
                    cancellationToken: AbortToken
                )
            );
            _time.Advance(TimeSpan.FromHours(1));
        }

        using var client = app.GetTestClient();
        var from = Uri.EscapeDataString(added[0].Added.ToString("O", CultureInfo.InvariantCulture));
        var before = Uri.EscapeDataString(added[2].Added.ToString("O", CultureInfo.InvariantCulture));

        // when
        var list = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + $"/published?from={from}&before={before}",
            AbortToken
        );

        // then
        list.GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("storageId").GetGuid())
            .Should()
            .Equal(added[1].StorageId, added[0].StorageId);
    }

    [Theory]
    [InlineData("published", "{}", "content", "required")]
    [InlineData("received", "{\"content\":\"x\",\"lane\":\"Sideways\"}", "lane", "enum")]
    [InlineData("published", "not json", "body", "json")]
    public async Task should_answer_a_validation_problem_for_a_bad_search(
        string list,
        string body,
        string field,
        string rule
    )
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        using var client = app.GetTestClient();

        // when
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _PostAsync(client, list, content);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        problem.RootElement.GetProperty("rules").GetProperty(field).GetString().Should().Be(rule);
    }

    private static Task<HttpResponseMessage> _SearchAsync(HttpClient client, string list, object body)
    {
        return _PostAsync(client, list, JsonContent.Create(body));
    }

    private static async Task<HttpResponseMessage> _PostAsync(HttpClient client, string list, HttpContent content)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{MessagingModuleTestHost.Api}/{list}/search", UriKind.Relative)
        )
        {
            Content = content,
        };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        return await client.SendAsync(request, AbortToken);
    }

    private static Message _Message(object value, string? correlationId = null)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString("N"),
            [Headers.MessageName] = "orders.placed",
        };

        if (correlationId is not null)
        {
            headers[Headers.CorrelationId] = correlationId;
        }

        return new Message(headers, value);
    }

    private static MediumMessage _Medium(MessageLane lane) =>
        new()
        {
            StorageId = Guid.Empty,
            Origin = _Message(new { Lane = lane.ToString() }),
            Content = string.Empty,
            Lane = lane,
        };

    private static MessagingStores _Stores(IDataStorage primary, IDataStorage outbox) =>
        new(
            "node-a",
            [new("primary", primary, HoldsReceived: true), new(_Outbox, outbox, HoldsReceived: false)],
            TimeSpan.FromMilliseconds(200)
        );

    private static IDataStorage _FailingOutbox()
    {
        var outbox = _QuietOutbox();
        outbox.GetMonitoringApi().Throws(new InvalidOperationException("billing is down"));

        return outbox;
    }

    private static IDataStorage _HangingOutbox()
    {
        var outbox = _QuietOutbox();
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring
            .ListMessagesAsync(
                Arg.Any<MessageListQuery>(),
                Arg.Any<MessageListPosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new ValueTask<MessageListPage>(
                Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>())
                    .ContinueWith(static _ => new MessageListPage { Items = [] }, TaskScheduler.Default)
            ));
        monitoring
            .CountMessagesAsync(Arg.Any<MessageListQuery>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<MessageCount>(
                Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>())
                    .ContinueWith(static _ => new MessageCount(0, false), TaskScheduler.Default)
            ));
        outbox.GetMonitoringApi().Returns(monitoring);

        return outbox;
    }

    // The change publisher polls every store in the background; an outbox that refuses those reads stays out of it.
    private static IDataStorage _QuietOutbox()
    {
        var outbox = Substitute.For<IDataStorage>();
        outbox
            .GetMessageChangeStartCursorAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("billing is down"));
        outbox
            .GetMessageChangesAsync(
                Arg.Any<MessageChangeCursor>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("billing is down"));

        return outbox;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => [.. _lines];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                lines.Enqueue(formatter(state, exception) + " " + exception);
            }
        }
    }
}
