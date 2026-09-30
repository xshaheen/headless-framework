// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tests;

/// <summary>
/// Proves that a 255-character <c>Idempotency-Key</c> header on a 500-character path is admitted, completed, and
/// replayed against SQL Server — the store key is the SHA-256 hex of the derived scope, so it always fits SQL
/// Server's 900-byte clustered key regardless of how long the header or path is.
/// </summary>
[Collection<ApiIdempotencySqlServerFixture>]
public sealed class IdempotencySqlServerLongKeyTests(ApiIdempotencySqlServerFixture fixture) : TestBase
{
    [Fact]
    public async Task should_admit_complete_and_replay_a_255_character_header_on_a_500_character_path()
    {
        var header = new string('k', 255);
        var path = "/" + new string('p', 499); // 500 characters total

        await using var app = await IdempotencyTestApp.CreateAsync(
            fixture.ConfigureStore,
            mapAdditionalEndpoints: a =>
                a.MapPost(
                    path,
                    async ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status201Created;
                        await ctx.Response.WriteAsync("ok");
                    }
                )
        );
        using var client = IdempotencyTestApp.CreateClient(app);

        var first = await _Post(client, path, header, "hello");
        var second = await _Post(client, path, header, "hello");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");

        var firstBody = await first.Content.ReadAsStringAsync(AbortToken);
        var secondBody = await second.Content.ReadAsStringAsync(AbortToken);
        secondBody.Should().Be(firstBody);
    }

    private static async Task<HttpResponseMessage> _Post(HttpClient client, string path, string key, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(body);
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);

        return await client.SendAsync(request, cancellationToken: AbortToken);
    }
}
