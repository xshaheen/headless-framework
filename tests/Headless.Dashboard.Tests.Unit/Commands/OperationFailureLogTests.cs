// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Tests.Authentication;
using static Tests.Commands.CommandTestHost;

namespace Tests.Commands;

/// <summary>
/// A chunk whose module throws may already have changed rows, so the shell records the attempt before the error
/// propagates.
/// </summary>
public sealed class OperationFailureLogTests : TestBase
{
    private const string _Operator = "Bearer diag-operator";
    private const string _Reason = "incident 42";

    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();
    private readonly FakeRunStore _store = new();
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_log_the_chunk_as_failed_when_the_module_throws_while_running_it()
    {
        _store.SeedFailed(200);
        await using var app = await CommandTestHost.StartAsync(
            new CommandTestHostOptions
            {
                Store = _store,
                Time = _time,
                Log = _log,
            },
            AbortToken
        );
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie);
        Module(app).ChunkFailure = new InvalidOperationException("The run store went away mid-chunk.");

        var chunk = async () =>
        {
            using var response = await PostAsync(
                client,
                $"{Base}/{FakeCommandModule.Requeue}/chunks",
                new
                {
                    token = scope.GetProperty("token").GetString(),
                    confirmed = true,
                    typedCount = scope.GetProperty("count").GetInt64(),
                },
                cookie,
                AbortToken
            );

            return response.StatusCode;
        };

        // The test server rethrows an unhandled endpoint error to the caller instead of answering 500.
        await chunk.Should().ThrowAsync<InvalidOperationException>().WithMessage("The run store went away mid-chunk.");
        Module(app).Chunks.Should().ContainSingle();
        var entry = _log
            .Entries.Should()
            .ContainSingle(static e => e.Action == OperatorLogAction.OperationChunk)
            .Subject;
        entry.Failed.Should().Be(500);
        entry.Done.Should().Be(0);
        entry.Reason.Should().Be(_Reason);
        entry.OperationId.Should().Be(scope.GetProperty("operationId").GetString());
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
