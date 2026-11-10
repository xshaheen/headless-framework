// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Time.Testing;
using Tests.Authentication;
using static Tests.Commands.CommandTestHost;

namespace Tests.Commands;

/// <summary>
/// A runner refuses a filter, mark, or cursor it does not accept with an <see cref="ArgumentException"/>; the shell
/// answers that as the operator's bad input, not as a server failure.
/// </summary>
public sealed class OperationInputTests : TestBase
{
    private const string _Operator = "Bearer diag-operator";
    private const string _Reason = "incident 42";

    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();
    private readonly FakeRunStore _store = new();
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_answer_400_on_the_filter_without_logging_when_the_runner_refuses_the_filter()
    {
        _store.SeedFailed(3);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
#pragma warning disable MA0015 // False positive: the exception stands in for one a runner throws, naming the runner's own parameter.
        Module(app).PrepareFailure = new ArgumentException("The filter names an unknown store 'nowhere'.", "filter");
#pragma warning restore MA0015

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}/confirm",
            new { filter = new { queue = "default" }, reason = _Reason },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadAsync(response, AbortToken);
        problem
            .GetProperty("errors")
            .GetProperty("filter")[0]
            .GetString()
            .Should()
            .Be("The filter names an unknown store 'nowhere'.");
        problem.GetProperty("rules").GetProperty("filter").GetString().Should().Be("invalid");
        _log.Entries.Should().NotContain(static e => e.Action == OperatorLogAction.OperationConfirmed);
    }

    [Fact]
    public async Task should_answer_400_on_the_cursor_without_logging_a_failed_chunk_when_the_runner_refuses_the_cursor()
    {
        _store.SeedFailed(3);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie);
#pragma warning disable MA0015 // False positive: the exception stands in for one a runner throws, naming the runner's own parameter.
        Module(app).ChunkFailure = new ArgumentException("'bogus' is not an Operation cursor.", "request");
#pragma warning restore MA0015

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}/chunks",
            new
            {
                token = scope.GetProperty("token").GetString(),
                confirmed = true,
                typedCount = scope.GetProperty("count").GetInt64(),
                cursor = "bogus",
            },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadAsync(response, AbortToken);
        // The developer-facing "(Parameter 'request')" suffix is dropped: the operator never named that parameter.
        problem
            .GetProperty("errors")
            .GetProperty("cursor")[0]
            .GetString()
            .Should()
            .Be("'bogus' is not an Operation cursor.");
        _log.Entries.Should().NotContain(static e => e.Action == OperatorLogAction.OperationChunk);
    }

    private Task<WebApplication> _StartAsync()
    {
        return CommandTestHost.StartAsync(
            new CommandTestHostOptions
            {
                Store = _store,
                Time = _time,
                Log = _log,
            },
            AbortToken
        );
    }

    private async Task<JsonElement> _ConfirmAsync(HttpClient client, string cookie)
    {
        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}/confirm",
            new { filter = new { queue = "default" }, reason = _Reason },
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadAsync(response, AbortToken);
    }
}
