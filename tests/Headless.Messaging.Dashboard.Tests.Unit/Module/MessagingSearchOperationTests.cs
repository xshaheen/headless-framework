// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests.Module;

/// <summary>
/// An Operation over the rows a content or correlation id search matches, as the shell confirms and runs it: the text
/// travels in the confirm's body and the encrypted token, the operator log records it redacted, no log line carries
/// it, and a search out of time refuses the confirm or stops the Operation incomplete.
/// </summary>
public sealed class MessagingSearchOperationTests : TestBase
{
    private const string _Commands = MessagingCommandHost.Commands;
    private const string _Outbox = "billing";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_re_execute_only_the_searched_rows_and_keep_the_text_out_of_every_log()
    {
        // given: two failures for good, one holding the order number; every log line of the host is captured
        const string searched = "ord-secret-991";
        using var logs = new CapturingLoggerProvider();
        await using var app = await MessagingCommandHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: services =>
                services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
        );
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var match = await MessagingCommandHost.InboxFailureAsync(
            storage,
            "order-1",
            """{"Headers":{},"Value":{"order":"ORD-SECRET-991"}}""",
            AbortToken
        );
        await MessagingCommandHost.InboxFailureAsync(
            storage,
            "order-2",
            """{"Headers":{},"Value":{"order":"ORD-100"}}""",
            AbortToken
        );
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when: the operator confirms Re-execute over the search, then runs it
        using var confirm = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/confirm",
            new
            {
                filter = new
                {
                    status = "Failed",
                    terminalOnly = true,
                    content = searched,
                },
                reason = "card service back",
            },
            cookie,
            AbortToken
        );
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmBody = await confirm.Content.ReadAsStringAsync(AbortToken);
        using var scope = JsonDocument.Parse(confirmBody);
        using var chunk = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/chunks",
            new
            {
                token = scope.RootElement.GetProperty("token").GetString(),
                cursor = (string?)null,
                confirmed = true,
                typedCount = 1,
            },
            cookie,
            AbortToken
        );

        // then: the scope held the matching row alone, and the chunk re-executed it
        scope.RootElement.GetProperty("count").GetInt64().Should().Be(1);
        chunk.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await MessagingCommandHost.ReadAsync(chunk, AbortToken)).GetProperty("rows");
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty("id").GetString().Should().EndWith(match.StorageId.ToString("D"));
        rows[0].GetProperty("outcome").GetString().Should().Be("done");

        // then: the operator log records the search as a marker, and no entry, log line, or answer carries the text
        _log.Entries.Should().HaveCount(2);
        _log.Entries.Should()
            .OnlyContain(entry =>
                entry.Scope!.Contains(MessagingCommands.RedactedSearch, StringComparison.Ordinal)
                && !_Mentions(entry.Scope, searched)
                && !_Mentions(entry.Reason, searched)
            );
        logs.Lines.Should().NotBeEmpty();
        logs.Lines.Should().NotContain(line => _Mentions(line, searched));
        confirmBody.Should().NotContainEquivalentOf(searched);
    }

    [Theory]
    [InlineData("content", 1_001)]
    [InlineData("correlationId", 201)]
    public async Task should_refuse_a_search_longer_than_the_list_takes_naming_the_field_and_not_the_text(
        string field,
        int length
    )
    {
        // given
        await using var app = await MessagingCommandHost.StartAsync(_time, _log, AbortToken);
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);
        var text = new string('q', length);

        // when
        using var confirm = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/confirm",
            new
            {
                filter = new Dictionary<string, object> { ["status"] = "Failed", [field] = text },
                reason = "card service back",
            },
            cookie,
            AbortToken
        );

        // then
        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await confirm.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain($"'{field}' takes at most");
        body.Should().NotContain(text);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_confirm_whose_search_runs_out_of_time_and_log_nothing()
    {
        // given: an outbox whose scope count never answers
        var operations = Substitute.For<IPublishedOperationsApi>();
        operations
            .CountOperationScopeAsync(Arg.Any<MessageOperationScope>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<MessageCount>(_HangAsync<MessageCount>(call.Arg<CancellationToken>())));
        await using var app = await _StartWithOutboxAsync(operations);
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var confirm = await _ConfirmResendAsync(client, cookie);

        // then: a refusal that says why and names no text, and nothing confirmed or logged
        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await confirm.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("The search ran out of time before it counted every matching message");
        body.Should().NotContainEquivalentOf("c-secret-42");
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_stop_an_operation_incomplete_when_a_chunk_search_runs_out_of_time()
    {
        // given: an outbox that counts and samples its scope, then never answers a chunk's read
        var operations = Substitute.For<IPublishedOperationsApi>();
        operations
            .CountOperationScopeAsync(Arg.Any<MessageOperationScope>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<MessageCount>(new MessageCount(3, Capped: false)));
        var reads = 0;
        operations
            .ReadOperationScopeAsync(
                Arg.Any<MessageOperationScope>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Interlocked.Increment(ref reads) == 1
                    ? new ValueTask<MessageOperationChunk>(new MessageOperationChunk { Targets = [] })
                    : new ValueTask<MessageOperationChunk>(
                        _HangAsync<MessageOperationChunk>(call.Arg<CancellationToken>())
                    )
            );
        await using var app = await _StartWithOutboxAsync(operations);
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);
        using var confirm = await _ConfirmResendAsync(client, cookie);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await MessagingCommandHost.ReadAsync(confirm, AbortToken)).GetProperty("token").GetString();

        // when
        using var chunk = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ResendId}/chunks",
            new
            {
                token,
                cursor = (string?)null,
                confirmed = true,
                typedCount = 3,
            },
            cookie,
            AbortToken
        );

        // then: the chunk changed nothing and says the Operation stopped incomplete; only the confirm is logged
        chunk.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await chunk.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("the Operation stopped incomplete");
        body.Should().NotContainEquivalentOf("c-secret-42");
        _log.Entries.Should()
            .ContainSingle()
            .Which.Should()
            .Match<OperatorLogEntry>(entry =>
                entry.Action == OperatorLogAction.OperationConfirmed
                && entry.Scope!.Contains(MessagingCommands.RedactedSearch, StringComparison.Ordinal)
            );
        await operations.DidNotReceiveWithAnyArgs().ResendAsync(default!, AbortToken);
    }

    private Task<HttpResponseMessage> _ConfirmResendAsync(HttpClient client, string cookie) =>
        MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ResendId}/confirm",
            new
            {
                filter = new
                {
                    store = _Outbox,
                    status = "Failed",
                    terminalOnly = true,
                    correlationId = "c-secret-42",
                },
                reason = "broker back",
            },
            cookie,
            AbortToken
        );

    private Task<Microsoft.AspNetCore.Builder.WebApplication> _StartWithOutboxAsync(IPublishedOperationsApi operations)
    {
        var outbox = _QuietOutbox();
        outbox.GetPublishedOperationsApi().Returns(operations);

        return MessagingCommandHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: services =>
            {
                services.Configure<MessagingDashboardOptions>(options =>
                    options.SearchTimeout = TimeSpan.FromMilliseconds(300)
                );
                services.AddSingleton(provider => new MessagingStores(
                    "node-a",
                    [
                        new("primary", provider.GetRequiredService<IDataStorage>(), HoldsReceived: true),
                        new(_Outbox, outbox, HoldsReceived: false),
                    ],
                    TimeSpan.FromMilliseconds(200)
                ));
            }
        );
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

    // A read that answers only by being cancelled, as a store scanning far more rows than the search's time allows.
    private static async Task<T> _HangAsync<T>(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        throw new UnreachableException();
    }

    private static bool _Mentions(string? text, string searched) =>
        text?.Contains(searched, StringComparison.OrdinalIgnoreCase) == true;
}
