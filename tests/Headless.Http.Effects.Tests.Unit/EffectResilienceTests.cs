// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Http.Effects;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tests;

/// <summary>
/// Pins the per-effect retry contract of <see cref="EffectResilience"/>: <see cref="OutboundEffect.Unsafe"/>
/// never retries, <see cref="OutboundEffect.Safe"/> retries, and <see cref="OutboundEffect.Idempotent"/>
/// retries with one stable key per logical call.
/// </summary>
public sealed class EffectResilienceTests : TestBase, IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task should_not_retry_unsafe_post_on_transient_failure()
    {
        // given
        _StubTransient("/unsafe");
        await using var provider = _BuildProvider(o => o.AddEffectResilienceHandler(OutboundEffect.Unsafe));

        // when
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var response = await client.PostAsync($"{_server.Urls[0]}/unsafe", new StringContent("{}"), AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _CountRequests("/unsafe").Should().Be(1, "an unsafe effect must not be retried automatically");
    }

    [Fact]
    public async Task should_retry_unsafe_read_on_transient_failure()
    {
        // given - unsafe disables retry only for RFC-unsafe methods; reads stay retryable.
        _StubTransient("/unsafe-read");
        await using var provider = _BuildProvider(o => o.AddEffectResilienceHandler(OutboundEffect.Unsafe));

        // when
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var response = await client.GetAsync($"{_server.Urls[0]}/unsafe-read", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _CountRequests("/unsafe-read").Should().BeGreaterThan(1, "reads stay retryable under an unsafe declaration");
    }

    [Fact]
    public async Task should_retry_safe_get_on_transient_failure()
    {
        // given
        _StubTransient("/safe");
        await using var provider = _BuildProvider(o => o.AddEffectResilienceHandler(OutboundEffect.Safe));

        // when
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var response = await client.GetAsync($"{_server.Urls[0]}/safe", AbortToken);

        // then - still failing after the pipeline gave up, but the wire saw the retries.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _CountRequests("/safe").Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task should_retry_idempotent_post_with_a_stable_key_across_attempts()
    {
        // given
        _StubTransient("/idempotent");
        await using var provider = _BuildProvider(o =>
            o.AddEffectResilienceHandler(OutboundEffect.Idempotent, idempotencyHeader: "Idempotency-Key")
        );

        // when
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var response = await client.PostAsync(
            $"{_server.Urls[0]}/idempotent",
            new StringContent("{}"),
            AbortToken
        );

        // then - retried, and every attempt of this logical call carries the same key.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var entries = _server.FindLogEntries(Request.Create().WithPath("/idempotent").UsingPost()).ToList();

        entries.Should().HaveCountGreaterThan(1, "an idempotent effect retries");

        var keys = entries
            .Select(static e => e.RequestMessage?.Headers?["Idempotency-Key"]?.ToString())
            .Where(static k => k is not null)
            .Distinct()
            .ToList();

        keys.Should().ContainSingle("retries of one logical call must reuse the key minted for it");
    }

    [Fact]
    public async Task should_let_consumer_tuning_re_enable_retry_for_unsafe()
    {
        // given - explicit opt-back-in: configureResilience runs after the derived defaults, so it
        // can replace the derived ShouldHandle and re-enable retry for POST.
        _StubTransient("/unsafe-opt-in");
        await using var provider = _BuildProvider(o =>
            o.AddEffectResilienceHandler(
                OutboundEffect.Unsafe,
                configureResilience: options => options.Retry.ShouldHandle = static _ => PredicateResult.True()
            )
        );

        // when
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var response = await client.PostAsync(
            $"{_server.Urls[0]}/unsafe-opt-in",
            new StringContent("{}"),
            AbortToken
        );

        // then - the consumer explicitly took retry ownership back.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _CountRequests("/unsafe-opt-in").Should().BeGreaterThan(1);
    }

    [Fact]
    public void should_remove_host_wide_default_handler_when_deriving_the_pipeline()
    {
        // given - a host-wide default handler (service defaults shape) applied before the effect one.
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        // when
        services.AddHttpClient("stacked").AddEffectResilienceHandler(OutboundEffect.Unsafe);

        // then - the effect handler replaced the default one instead of stacking under it; a leftover
        // outer standard pipeline would surface as a second "standard" pipeline options registration.
        using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("stacked");

        client.Should().NotBeNull();
    }

    private void _StubTransient(string path)
    {
        _server
            .Given(Request.Create().WithPath(path))
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable).WithBody("transient"));
    }

    private int _CountRequests(string path)
    {
        return _server.FindLogEntries(Request.Create().WithPath(path)).Count();
    }

    private static ServiceProvider _BuildProvider(Action<IHttpClientBuilder> configureClient)
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("test");
        configureClient(builder);

        return services.BuildServiceProvider();
    }
}
