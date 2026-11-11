// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using System.Text.Json;
using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// An Operation another node runs over node B's rows is held to node B's own limits and checks: the row limit, the
/// chunk size, the reason, the arguments, and the body's shape, each refused with the error a local call gets.
/// </summary>
public sealed class ForwardingOperationLimitTests : TestBase
{
    private const string _Api = "/headless/api/messaging";
    private const string _Scope = _Api + "/peer/operations/" + MessagingCommands.ReExecuteId + "/scope";
    private const string _Chunk = _Api + "/peer/operations/" + MessagingCommands.ReExecuteId + "/chunk";
    private const string _Confirm = MessagingCommandHost.Commands + "/" + MessagingCommands.ReExecuteId + "/confirm";
    private const string _LocalChunk = MessagingCommandHost.Commands + "/" + MessagingCommands.ReExecuteId + "/chunks";
    private const string _Reason = "card processor recovered";

    // The shell's limit: the most rows one Operation may act on.
    private const int _MaxRows = 100_000;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly RecordingOperatorLog _logA = new();
    private readonly RecordingOperatorLog _logB = new();

    public ForwardingOperationLimitTests()
    {
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
    }

    [Fact]
    public async Task should_refuse_a_relayed_scope_over_100000_rows_with_the_error_a_local_confirm_answers()
    {
        // given: node B's store counts more rows than the limit
        var inbox = _InboxPastTheLimit();
        await using var nodeB = await _StartBAsync(services => _UseStore(services, inbox));
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);

        // when: the calling node asks for a count far past the limit
        using var relayed = await _PeerAsync(
            clientB,
            _Scope,
            """{"filter":{"status":"Failed"},"countLimit":2147483647,"sampleSize":100000}"""
        );
        using var local = await MessagingCommandHost.PostAsync(
            clientB,
            _Confirm,
            new { filter = new { status = "Failed" }, reason = _Reason },
            cookie,
            AbortToken
        );

        // then
        var problem = await _ProblemAsync(relayed);
        problem.Should().Be(await _ProblemAsync(local));
        problem.Rules.Should().Be("""{"filter":"maxCount"}""");
        problem.Detail.Should().Be("The filter matches more than 100,000 rows. Narrow the filter and confirm again.");
        await inbox
            .Received()
            .CountOperationScopeAsync(Arg.Any<MessageOperationScope>(), _MaxRows, Arg.Any<CancellationToken>());
        await inbox
            .DidNotReceive()
            .CountOperationScopeAsync(
                Arg.Any<MessageOperationScope>(),
                Arg.Is<int>(cap => cap != _MaxRows),
                Arg.Any<CancellationToken>()
            );
        _logB.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_the_confirm_on_node_a_as_a_local_one_when_node_b_holds_more_than_100000_rows()
    {
        // given
        await using var nodeB = await _StartBAsync(services => _UseStore(services, _InboxPastTheLimit()));
        await using var nodeA = await ForwardingHost.StartAsync(
            "node-a",
            _network,
            _discoveryA,
            _time,
            _logA,
            AbortToken
        );
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.PostAsync(
            client,
            _Confirm,
            new { filter = new { node = "node-b", status = "Failed" }, reason = _Reason },
            cookie,
            AbortToken
        );

        // then
        var problem = await _ProblemAsync(response);
        problem.Rules.Should().Be("""{"filter":"maxCount"}""");
        problem.Detail.Should().Be("The filter matches more than 100,000 rows. Narrow the filter and confirm again.");
        _logA.Entries.Should().BeEmpty();
        _logB.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_relayed_chunk_without_a_reason_with_the_error_a_local_confirm_answers()
    {
        // given
        await using var nodeB = await _StartBAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);
        var token = await _PeerScopeAsync(clientB);

        // when
        using var relayed = await _PeerAsync(clientB, _Chunk, JsonSerializer.Serialize(new { token }));
        using var local = await MessagingCommandHost.PostAsync(
            clientB,
            _Confirm,
            new { filter = new { status = "Failed" } },
            cookie,
            AbortToken
        );

        // then
        var problem = await _ProblemAsync(relayed);
        problem.Errors.Should().Be((await _ProblemAsync(local)).Errors);
        problem.Rules.Should().Be("""{"reason":"required"}""");
        await _ShouldChangeNothingAsync(storageB);
    }

    [Fact]
    public async Task should_refuse_a_relayed_chunk_with_a_reason_over_1000_characters_as_a_local_confirm_does()
    {
        // given
        await using var nodeB = await _StartBAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);
        var token = await _PeerScopeAsync(clientB);
        var reason = new string('r', 1_001);

        // when
        using var relayed = await _PeerAsync(clientB, _Chunk, JsonSerializer.Serialize(new { token, reason }));
        using var local = await MessagingCommandHost.PostAsync(
            clientB,
            _Confirm,
            new { filter = new { status = "Failed" }, reason },
            cookie,
            AbortToken
        );

        // then
        var problem = await _ProblemAsync(relayed);
        problem.Errors.Should().Be((await _ProblemAsync(local)).Errors);
        problem.Rules.Should().Be("""{"reason":"maxLength"}""");
        await _ShouldChangeNothingAsync(storageB);
    }

    [Fact]
    public async Task should_refuse_a_relayed_chunk_with_an_undeclared_argument_as_a_local_confirm_does()
    {
        // given
        await using var nodeB = await _StartBAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);
        var token = await _PeerScopeAsync(clientB);
        var arguments = new { force = true };

        // when
        using var relayed = await _PeerAsync(
            clientB,
            _Chunk,
            JsonSerializer.Serialize(
                new
                {
                    token,
                    reason = _Reason,
                    arguments,
                }
            )
        );
        using var local = await MessagingCommandHost.PostAsync(
            clientB,
            _Confirm,
            new
            {
                filter = new { status = "Failed" },
                reason = _Reason,
                arguments,
            },
            cookie,
            AbortToken
        );

        // then
        var problem = await _ProblemAsync(relayed);
        problem.Errors.Should().Be((await _ProblemAsync(local)).Errors);
        problem.Rules.Should().Be("""{"arguments.force":"additionalProperties"}""");
        await _ShouldChangeNothingAsync(storageB);
    }

    [Theory]
    [InlineData("""{"token":5}""", "token", "type")]
    [InlineData("""{"token":"x","reason":["a"]}""", "reason", "type")]
    [InlineData("""{"token":"x","cursor":{}}""", "cursor", "type")]
    [InlineData("[]", "body", "json")]
    public async Task should_refuse_a_relayed_chunk_of_the_wrong_shape_with_the_error_a_local_chunk_answers(
        string body,
        string field,
        string rule
    )
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);

        // when
        using var relayed = await _PeerAsync(clientB, _Chunk, body);
        using var local = await _PostRawAsync(clientB, _LocalChunk, body, cookie);

        // then
        var problem = await _ProblemAsync(relayed);
        problem.Errors.Should().Be((await _ProblemAsync(local)).Errors);
        problem.Rules.Should().Be($$"""{"{{field}}":"{{rule}}"}""");
        _logB.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_relayed_chunk_whose_scope_node_b_did_not_sign_even_with_a_token_node_b_confirmed()
    {
        // given: a token node B's own shell signed for a local Operation over the same rows
        await using var nodeB = await _StartBAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);
        using var confirm = await MessagingCommandHost.PostAsync(
            clientB,
            _Confirm,
            new { filter = new { status = "Failed" }, reason = _Reason },
            cookie,
            AbortToken
        );
        var shellToken = (await MessagingCommandHost.ReadAsync(confirm, AbortToken)).GetProperty("token").GetString();

        // when
        using var shellTokenSent = await _PeerAsync(
            clientB,
            _Chunk,
            JsonSerializer.Serialize(new { token = shellToken, reason = _Reason })
        );
        using var forged = await _PeerAsync(
            clientB,
            _Chunk,
            JsonSerializer.Serialize(new { token = "not-a-token", reason = _Reason })
        );

        // then
        foreach (var refused in new[] { shellTokenSent, forged })
        {
            var problem = await _ProblemAsync(refused);
            problem.Rules.Should().Be("""{"token":"invalid"}""");
            problem.Detail.Should().Be("The Operation token is not valid. Confirm the Operation again.");
        }

        await _ShouldChangeNothingAsync(storageB);
    }

    [Fact]
    public async Task should_refuse_a_relayed_chunk_sent_to_another_command_with_the_error_a_local_chunk_answers()
    {
        // given: a scope node B signed for re-execute, and a local token node B's shell confirmed for re-execute
        await using var nodeB = await _StartBAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);
        var peerToken = await _PeerScopeAsync(clientB);
        var localToken = await _LocalConfirmAsync(clientB, cookie);

        // when: each is sent to purge's chunk endpoint
        using var relayed = await _PeerAsync(
            clientB,
            _Api + "/peer/operations/" + MessagingCommands.PurgeId + "/chunk",
            JsonSerializer.Serialize(new { token = peerToken, reason = _Reason })
        );
        using var local = await MessagingCommandHost.PostAsync(
            clientB,
            MessagingCommandHost.Commands + "/" + MessagingCommands.PurgeId + "/chunks",
            new { token = localToken, confirmed = true },
            cookie,
            AbortToken
        );

        // then
        var problem = await _ProblemAsync(relayed);
        problem.Should().Be(await _ProblemAsync(local));
        problem.Rules.Should().Be("""{"token":"scope"}""");
        await _ShouldChangeNothingAsync(storageB);
    }

    [Fact]
    public async Task should_refuse_a_relayed_chunk_after_the_scope_expired_with_the_error_a_local_chunk_answers()
    {
        // given
        await using var nodeB = await _StartBAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);
        var peerToken = await _PeerScopeAsync(clientB);
        var localToken = await _LocalConfirmAsync(clientB, cookie);

        // when: the token lifetime passes before the first chunk; the operator signs in again, as the session idled out
        _time.Advance(TimeSpan.FromHours(1));
        var later = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);
        using var relayed = await _PeerAsync(
            clientB,
            _Chunk,
            JsonSerializer.Serialize(new { token = peerToken, reason = _Reason })
        );
        using var local = await MessagingCommandHost.PostAsync(
            clientB,
            _LocalChunk,
            new { token = localToken, confirmed = true },
            later,
            AbortToken
        );

        // then
        var problem = await _ProblemAsync(relayed);
        problem.Should().Be(await _ProblemAsync(local));
        problem.Rules.Should().Be("""{"token":"expired"}""");
        await _ShouldChangeNothingAsync(storageB);
    }

    [Fact]
    public async Task should_run_a_relayed_chunk_at_the_shells_chunk_size_whatever_size_the_calling_node_sends()
    {
        // given: three rows in scope, and a calling node that asks for one row per chunk
        await using var nodeB = await _StartBAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();

        foreach (var messageId in new[] { "order-1", "order-2", "order-3" })
        {
            await MessagingCommandHost.InboxFailureAsync(storageB, messageId, AbortToken);
        }

        using var clientB = MessagingCommandHost.Client(nodeB);
        var token = await _PeerScopeAsync(clientB);

        // when
        using var response = await _PeerAsync(
            clientB,
            _Chunk,
            JsonSerializer.Serialize(
                new
                {
                    token,
                    reason = _Reason,
                    chunkSize = 1,
                }
            )
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var chunk = await MessagingCommandHost.ReadAsync(response, AbortToken);
        chunk.GetProperty("rows").GetArrayLength().Should().Be(3);
        var entry = _logB.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(OperatorLogAction.OperationChunk);
        entry.Actor.Should().Be("ops via node-a");
        entry.Reason.Should().Be(_Reason);
        entry.Done.Should().Be(3);
        entry.Scope.Should().Be("""{"status":"Failed"}""");
    }

    [Fact]
    public async Task should_refuse_a_relayed_scope_without_a_filter_with_the_error_a_local_confirm_answers()
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);

        // when
        using var relayed = await _PeerAsync(clientB, _Scope, "{}");
        using var local = await MessagingCommandHost.PostAsync(
            clientB,
            _Confirm,
            new { reason = _Reason },
            cookie,
            AbortToken
        );

        // then
        var problem = await _ProblemAsync(relayed);
        problem.Errors.Should().Be((await _ProblemAsync(local)).Errors);
        problem.Rules.Should().Be("""{"filter":"required"}""");
    }

    // Node B's store, whose received-row count runs past the limit whatever cap it is given.
    private static IInboxOperationsApi _InboxPastTheLimit()
    {
        var inbox = Substitute.For<IInboxOperationsApi>();
        inbox
            .CountOperationScopeAsync(Arg.Any<MessageOperationScope>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<MessageCount>(MessageCount.Of(_MaxRows + 1L, call.ArgAt<int>(1))));
        inbox
            .ReadOperationScopeAsync(
                Arg.Any<MessageOperationScope>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<MessageOperationChunk>(new MessageOperationChunk { Targets = [] }));

        return inbox;
    }

    private static void _UseStore(IServiceCollection services, IInboxOperationsApi inbox)
    {
        var storage = Substitute.For<IDataStorage>();
        storage.GetInboxOperationsApi().Returns(inbox);
        services.Replace(
            ServiceDescriptor.Singleton(
                new MessagingStores(
                    "node-b",
                    [new MessagingStore("primary", storage, HoldsReceived: true)],
                    TimeSpan.FromSeconds(5)
                )
            )
        );
    }

    // The mark node B signs at the scope step over its failed rows, which the calling node sends back as the token.
    private async Task<string> _PeerScopeAsync(HttpClient clientB)
    {
        using var response = await _PeerAsync(clientB, _Scope, """{"filter":{"status":"Failed"}}""");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("highWaterMark").GetString()!;
    }

    // The token node B's own shell signs when its operator confirms re-execute over the failed rows.
    private async Task<string> _LocalConfirmAsync(HttpClient clientB, string cookie)
    {
        using var response = await MessagingCommandHost.PostAsync(
            clientB,
            _Confirm,
            new { filter = new { status = "Failed" }, reason = _Reason },
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("token").GetString()!;
    }

    // Sends body to node B as node A forwards an Operation call, signed and at operate access.
    private async Task<HttpResponseMessage> _PeerAsync(HttpClient client, string path, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.HopHeader, "node-a");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.ActorHeader, "ops");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.AccessHeader, ForwardingProtocol.AccessOperate);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        await ForwardingHost.SignAsync(request, _time, AbortToken);

        return await client.SendAsync(request, AbortToken);
    }

    private async Task<HttpResponseMessage> _PostRawAsync(HttpClient client, string path, string body, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        return await client.SendAsync(request, AbortToken);
    }

    private async Task<Problem> _ProblemAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await MessagingCommandHost.ReadAsync(response, AbortToken);

        return new Problem(
            problem.GetProperty("errors").GetRawText(),
            problem.GetProperty("rules").GetRawText(),
            problem.TryGetProperty("detail", out var detail) ? detail.GetString() : null
        );
    }

    private async Task _ShouldChangeNothingAsync(IDataStorage storage)
    {
        _logB.Entries.Should().NotContain(entry => entry.Action == OperatorLogAction.OperationChunk);
        (
            await storage
                .GetMonitoringApi()
                .CountMessagesAsync(new MessageListQuery { MessageType = MessageType.Subscribe }, 10, AbortToken)
        )
            .Count.Should()
            .Be(1);
    }

    private async Task<WebApplication> _StartBAsync(Action<IServiceCollection>? configure = null)
    {
        var app = await ForwardingHost.StartAsync(
            "node-b",
            _network,
            new StaticDiscovery(),
            _time,
            _logB,
            AbortToken,
            options => options.ForwardedAccess = MessagingForwardedAccess.Operate,
            configure
        );
        _network.Add("node-b", app);

        return app;
    }

    private sealed record Problem(string Errors, string Rules, string? Detail);
}
