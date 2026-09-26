// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api.Idempotency;
using Headless.Constants;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

// CA2025: `_PostAsync` builds an `HttpRequestMessage` under `using var` and awaits `SendAsync` inline, so the request
// disposes only after the SendAsync task completes. The in-flight test stores the returned Task to interleave with
// another request, which the analyzer cannot see is safe.
#pragma warning disable CA2025

namespace Tests;

/// <summary>
/// The idempotency middleware on the in-memory provider, with no database and no container: replay, in-flight
/// rejection and waiting, and the release of a failed attempt. The in-memory provider's own semantics are covered by
/// its conformance run; this proves the HTTP adapter composes with it end to end.
/// </summary>
public sealed class IdempotencyInMemoryEndToEndTests : TestBase
{
    [Fact]
    public async Task should_replay_cached_response_on_identical_retry()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _PostAsync(client, "/echo", key, "hello");
        var second = await _PostAsync(client, "/echo", key, "hello");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        (await second.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Be(await first.Content.ReadAsStringAsync(AbortToken), "the handler ran once and the retry replayed it");
        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public async Task should_409_the_loser_when_concurrent_requests_with_reject_strategy()
    {
        var key = _UniqueKey();
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await _CreateAppAsync(handlerGate: gate);
        using var client = IdempotencyTestApp.CreateClient(app);

        var winnerTask = _PostAsync(client, "/echo", key, "hello");
        await gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(5));
        var loser = await _PostAsync(client, "/echo", key, "hello");

        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await loser.Content.ReadAsStringAsync(AbortToken)).Should().Contain("g:idempotency_in_flight");

        gate.Release();
        (await winnerTask).StatusCode.Should().Be(HttpStatusCode.Created);
        gate.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task should_block_the_loser_until_the_winner_completes_then_replay_with_wait_and_replay()
    {
        var key = _UniqueKey();
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await _CreateAppAsync(
            options =>
            {
                options.InFlightStrategy = InFlightStrategy.WaitAndReplay;
                options.InFlightLockTimeout = TimeSpan.FromSeconds(15);
            },
            gate
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        var winnerTask = _PostAsync(client, "/echo", key, "hello");
        await gate.WaitForInvocationsAsync(1, TimeSpan.FromSeconds(5));
        var loserTask = _PostAsync(client, "/echo", key, "hello");
        await Task.Delay(500, AbortToken);
        loserTask.IsCompleted.Should().BeFalse("the loser waits on the winner's admission");

        gate.Release();
        var winner = await winnerTask;
        var loser = await loserTask;

        winner.StatusCode.Should().Be(HttpStatusCode.Created);
        loser.StatusCode.Should().Be(HttpStatusCode.Created);
        loser.Headers.Should().Contain(h => h.Key == HttpHeaderNames.IdempotentReplayed);
        (await loser.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Be(await winner.Content.ReadAsStringAsync(AbortToken));
        gate.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task should_release_and_rerun_the_handler_when_the_response_is_5xx()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _PostAsync(client, "/status?code=503", key, "");
        var second = await _PostAsync(client, "/status?code=503", key, "");

        first.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        second
            .Headers.Contains(HttpHeaderNames.IdempotentReplayed)
            .Should()
            .BeFalse("a 5xx releases the admission, so the retry runs the handler again");
    }

    private static Task<WebApplication> _CreateAppAsync(
        Action<IdempotencyOptions>? configure = null,
        IdempotencyTestApp.TestHandlerGate? handlerGate = null
    )
    {
        return IdempotencyTestApp.CreateAsync(
            static services =>
                services.AddHeadlessIdempotency(static setup =>
                {
                    setup.UseInMemory();
                    setup.ConfigureOptions(static options => options.PurgeInterval = null);
                }),
            configure,
            handlerGate: handlerGate
        );
    }

    private static string _UniqueKey()
    {
        return $"k-{Guid.NewGuid():N}";
    }

    private static async Task<HttpResponseMessage> _PostAsync(HttpClient client, string path, string key, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(body);
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);

        return await client.SendAsync(request, cancellationToken: AbortToken);
    }
}
