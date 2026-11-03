// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api;
using Headless.Http;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA2025 // False positive: _PostAsync awaits SendAsync before its request disposes; tests only hold the outer task.

namespace Tests;

/// <summary>
/// Per-endpoint opt-out from response replay on a real host and the in-memory provider: the MVC attribute, the Minimal
/// API builder method, a route group's convention, and endpoints that did not opt out still replaying.
/// </summary>
public sealed class IdempotencyResponseReplayOptOutTests : TestBase
{
    [Fact]
    public async Task should_run_the_action_on_every_retry_when_the_mvc_action_opted_out()
    {
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var key = _UniqueKey();

        var first = await _PostAsync(client, "/mvc/opted-out", key);
        var second = await _PostAsync(client, "/mvc/opted-out", key);

        await _ShouldHaveRunTwiceAsync(first, second);
    }

    [Fact]
    public async Task should_run_every_action_on_every_retry_when_the_controller_opted_out()
    {
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var key = _UniqueKey();

        var first = await _PostAsync(client, "/mvc-opted-out/run", key);
        var second = await _PostAsync(client, "/mvc-opted-out/run", key);

        await _ShouldHaveRunTwiceAsync(first, second);
    }

    [Fact]
    public async Task should_replay_an_mvc_action_when_only_its_sibling_opted_out()
    {
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var key = _UniqueKey();

        var first = await _PostAsync(client, "/mvc/replayed", key);
        var second = await _PostAsync(client, "/mvc/replayed", key);

        await _ShouldHaveReplayedAsync(first, second);
    }

    [Fact]
    public async Task should_not_require_a_key_when_the_mvc_action_opted_out_of_a_map_controllers_convention()
    {
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var optedOut = await _PostAsync(client, "/mvc/opted-out", key: null);
        var replayed = await _PostAsync(client, "/mvc/replayed", key: null);

        optedOut.StatusCode.Should().Be(HttpStatusCode.OK);
        replayed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "MapControllers().RequireIdempotencyKey() applies");
        _Contract(app, "mvc/opted-out").Should().BeEmpty();
        _Contract(app, "mvc/replayed").Should().ContainSingle();
    }

    [Fact]
    public async Task should_run_the_handler_on_every_retry_when_the_minimal_api_endpoint_opted_out()
    {
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var key = _UniqueKey();

        var first = await _PostAsync(client, "/minimal/opted-out", key);
        var second = await _PostAsync(client, "/minimal/opted-out", key);

        await _ShouldHaveRunTwiceAsync(first, second);
    }

    [Fact]
    public async Task should_replay_a_minimal_api_endpoint_when_it_did_not_opt_out()
    {
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var key = _UniqueKey();

        var first = await _PostAsync(client, "/minimal/replayed", key);
        var second = await _PostAsync(client, "/minimal/replayed", key);

        await _ShouldHaveReplayedAsync(first, second);
    }

    [Fact]
    public async Task should_run_every_endpoint_of_the_group_on_every_retry_when_the_group_opted_out()
    {
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var key = _UniqueKey();

        var first = await _PostAsync(client, "/opted-out-group/run", key);
        var second = await _PostAsync(client, "/opted-out-group/run", key);

        await _ShouldHaveRunTwiceAsync(first, second);
    }

    [Fact]
    public async Task should_not_require_a_key_or_document_one_when_the_endpoint_opted_out_of_its_group_requirement()
    {
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);

        var optedOut = await _PostAsync(client, "/keyed-group/opted-out", key: null);
        var keyed = await _PostAsync(client, "/keyed-group/keyed", key: null);

        optedOut.StatusCode.Should().Be(HttpStatusCode.OK);
        keyed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the group's RequireIdempotencyKey() applies");
        _Contract(app, "/keyed-group/opted-out").Should().BeEmpty();
        _Contract(app, "/keyed-group/keyed").Should().ContainSingle();
    }

    private static async Task _ShouldHaveRunTwiceAsync(HttpResponseMessage first, HttpResponseMessage second)
    {
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.Contains(HttpHeaderNames.IdempotentReplayed).Should().BeFalse();
        (await second.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .NotBe(await first.Content.ReadAsStringAsync(AbortToken), "the handler ran again instead of replaying");
    }

    private static async Task _ShouldHaveReplayedAsync(HttpResponseMessage first, HttpResponseMessage second)
    {
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");
        (await second.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Be(await first.Content.ReadAsStringAsync(AbortToken), "the handler ran once and the retry replayed it");
    }

    private static IEnumerable<HttpContractMetadata> _Contract(WebApplication app, string route)
    {
        return app
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => string.Equals(endpoint.RoutePattern.RawText, route, StringComparison.Ordinal))
            .Metadata.OfType<HttpContractMetadata>();
    }

    private static Task<WebApplication> _CreateAppAsync()
    {
        return IdempotencyTestApp.CreateAsync(
            static services =>
                services.AddHeadlessIdempotency(static setup =>
                {
                    setup.UseInMemory();
                    setup.ConfigureOptions(static options => options.PurgeInterval = null);
                }),
            mapAdditionalEndpoints: static app =>
            {
                app.MapControllers().RequireIdempotencyKey();

                app.MapPost("/minimal/opted-out", _Invocation).DisableResponseReplay();
                app.MapPost("/minimal/replayed", _Invocation);

                app.MapGroup("/opted-out-group").DisableResponseReplay().MapPost("/run", _Invocation);

                var keyed = app.MapGroup("/keyed-group").RequireIdempotencyKey();
                keyed.MapPost("/opted-out", _Invocation).DisableResponseReplay();
                keyed.MapPost("/keyed", _Invocation);
            },
            configureServices: static services =>
                services
                    .AddControllers()
                    .ConfigureApplicationPartManager(static manager =>
                    {
                        // Only this suite's controllers, whatever the entry assembly is.
                        manager.ApplicationParts.Clear();
                        manager.ApplicationParts.Add(new AssemblyPart(typeof(ReplayOptOutTestController).Assembly));
                    })
        );
    }

    // A fresh value per invocation, so equal bodies across two requests prove a replay.
    private static string _Invocation() => Guid.NewGuid().ToString("N");

    private static string _UniqueKey()
    {
        return $"k-{Guid.NewGuid():N}";
    }

    private static async Task<HttpResponseMessage> _PostAsync(HttpClient client, string path, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent("");

        if (key is not null)
        {
            request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);
        }

        return await client.SendAsync(request, cancellationToken: AbortToken);
    }
}

[ApiController]
[Route("mvc")]
public sealed class ReplayOptOutTestController : ControllerBase
{
    [HttpPost("opted-out")]
    [DisableResponseReplay]
    public string OptedOut() => Guid.NewGuid().ToString("N");

    [HttpPost("replayed")]
    public string Replayed() => Guid.NewGuid().ToString("N");
}

[ApiController]
[Route("mvc-opted-out")]
[DisableResponseReplay]
public sealed class OptedOutTestController : ControllerBase
{
    [HttpPost("run")]
    public string Run() => Guid.NewGuid().ToString("N");
}
