// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Caching;
using Headless.Http;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA2025 // False positive: _PostAsync awaits SendAsync before its request disposes; tests only hold the outer task.

namespace Tests;

/// <summary>
/// The idempotency middleware on the cache provider over the in-memory cache: replay, in-flight rejection, and the
/// release of a failed attempt. The provider's own semantics, including a real Redis, are covered by its test projects;
/// this proves the HTTP adapter composes with an optimistic store end to end.
/// </summary>
public sealed class IdempotencyCacheEndToEndTests : TestBase
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
        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public async Task should_409_the_loser_while_the_winner_runs()
    {
        var key = _UniqueKey();
        var gate = new IdempotencyTestApp.TestHandlerGate();
        await using var app = await _CreateAppAsync(gate);
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

    private static Task<WebApplication> _CreateAppAsync(IdempotencyTestApp.TestHandlerGate? handlerGate = null)
    {
        return IdempotencyTestApp.CreateAsync(
            static services =>
            {
                services.AddHeadlessCaching(static setup => setup.UseInMemory());
                services.AddHeadlessIdempotency(static setup =>
                {
                    setup.UseCache();
                    setup.ConfigureOptions(static options => options.PurgeInterval = null);
                });
            },
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
