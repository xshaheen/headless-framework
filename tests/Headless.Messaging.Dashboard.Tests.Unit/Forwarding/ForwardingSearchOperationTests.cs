// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// An Operation over a content search that node A forwards to node B: the text crosses inside node B's signed scope,
/// and node B, like a local shell, records the search as a marker and writes no log line that carries it.
/// </summary>
public sealed class ForwardingSearchOperationTests : TestBase
{
    private const string _Commands = MessagingCommandHost.Commands;
    private const string _Searched = "ord-secret-991";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly RecordingOperatorLog _logA = new();
    private readonly RecordingOperatorLog _logB = new();

    public ForwardingSearchOperationTests()
    {
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
    }

    [Fact]
    public async Task should_keep_the_searched_text_out_of_node_b_logs_when_node_a_forwards_a_search_operation()
    {
        // given: two failures on node B, one holding the order number; every log line of node B is captured
        using var logsB = new CapturingLoggerProvider();
        await using var nodeB = await _StartBAsync(logsB);
        await using var nodeA = await ForwardingHost.StartAsync(
            "node-a",
            _network,
            _discoveryA,
            _time,
            _logA,
            AbortToken
        );
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var match = await MessagingCommandHost.InboxFailureAsync(
            storageB,
            "order-1",
            """{"Headers":{},"Value":{"order":"ORD-SECRET-991"}}""",
            AbortToken
        );
        await MessagingCommandHost.InboxFailureAsync(
            storageB,
            "order-2",
            """{"Headers":{},"Value":{"order":"ORD-100"}}""",
            AbortToken
        );
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when: node A's operator confirms Re-execute over the search on node B, then runs it
        using var confirm = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/confirm",
            new
            {
                filter = new
                {
                    node = "node-b",
                    status = "Failed",
                    terminalOnly = true,
                    content = _Searched,
                },
                reason = "card service back",
            },
            cookie,
            AbortToken
        );
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var scope = await MessagingCommandHost.ReadAsync(confirm, AbortToken);
        using var chunk = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/chunks",
            new
            {
                token = scope.GetProperty("token").GetString(),
                cursor = (string?)null,
                confirmed = true,
                typedCount = 1,
            },
            cookie,
            AbortToken
        );

        // then: node B scoped and re-executed the matching row alone
        scope.GetProperty("count").GetInt64().Should().Be(1);
        chunk.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await MessagingCommandHost.ReadAsync(chunk, AbortToken)).GetProperty("rows");
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty("id").GetString().Should().EndWith(match.StorageId.ToString("D"));

        // then: node B records the chunk with the search as a marker, and none of its log lines carries the text
        var chunksB = _logB.Entries.Where(entry => entry.Action == OperatorLogAction.OperationChunk).ToList();
        chunksB.Should().ContainSingle();
        chunksB[0].Actor.Should().Be("ops via node-a");
        _logB
            .Entries.Should()
            .OnlyContain(entry =>
                entry.Scope!.Contains(MessagingCommands.RedactedSearch, StringComparison.Ordinal)
                && !_Mentions(entry.Scope, _Searched)
                && !_Mentions(entry.Reason, _Searched)
            );
        logsB.Lines.Should().NotBeEmpty();
        logsB.Lines.Should().NotContain(line => _Mentions(line, _Searched));

        // then: node A's own record of the Operation, built from node B's scope, is redacted the same way
        _logA.Entries.Should().NotBeEmpty();
        _logA.Entries.Should().OnlyContain(entry => !_Mentions(entry.Scope, _Searched));
    }

    private async Task<WebApplication> _StartBAsync(CapturingLoggerProvider logs)
    {
        var app = await ForwardingHost.StartAsync(
            "node-b",
            _network,
            new StaticDiscovery(),
            _time,
            _logB,
            AbortToken,
            options => options.ForwardedAccess = MessagingForwardedAccess.Operate,
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
        );
        _network.Add("node-b", app);

        return app;
    }

    private static bool _Mentions(string? text, string searched) =>
        text?.Contains(searched, StringComparison.OrdinalIgnoreCase) == true;
}
