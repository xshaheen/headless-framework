// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Http.Resilience;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
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
        using var response = await _PostAsync(client, "/unsafe");

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
        using var response = await _PostAsync(client, "/idempotent");

        // then - retried, and every attempt of this logical call carries the same key.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var entries = _server.FindLogEntries(Request.Create().WithPath("/idempotent").UsingPost()).ToList();

        entries.Should().HaveCountGreaterThan(1, "an idempotent effect retries");

        var keys = entries
            .Select(static e => e.RequestMessage?.Headers?["Idempotency-Key"]?.ToString())
            .Where(static k => k is not null)
            .Distinct(StringComparer.Ordinal)
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
        using var response = await _PostAsync(client, "/unsafe-opt-in");

        // then - the consumer explicitly took retry ownership back.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _CountRequests("/unsafe-opt-in").Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task should_retry_post_when_a_per_client_opt_out_stacks_under_a_host_wide_handler()
    {
        // Characterizes the library behavior behind the stacking defect: a second standard handler is added, not
        // swapped in, so the host-wide one becomes an outer pipeline whose stock retry ignores the inner opt-out.
        // This is the shape of every hand-written opt-out (SMS, Captcha, the first Paymob fix) under ServiceDefaults.
        _StubTransient("/stacked-opt-out");
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services
            .AddHttpClient("test")
            .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
        await using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var response = await _PostAsync(client, "/stacked-opt-out");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _CountRequests("/stacked-opt-out").Should().BeGreaterThan(1, "the outer host-wide pipeline retries the POST");
    }

    [Fact]
    public async Task should_not_retry_unsafe_post_when_a_host_wide_handler_is_configured()
    {
        _StubTransient("/stacked-effect");
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddHttpClient("test").AddEffectResilienceHandler(OutboundEffect.Unsafe);
        await using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var response = await _PostAsync(client, "/stacked-effect");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _CountRequests("/stacked-effect").Should().Be(1, "the derived pipeline removes the host-wide handler");
    }

    private void _StubTransient(string path)
    {
        _server
            .Given(Request.Create().WithPath(path))
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable).WithBody("transient"));
    }

    private int _CountRequests(string path)
    {
        return _server.FindLogEntries(Request.Create().WithPath(path)).Count;
    }

    private async Task<HttpResponseMessage> _PostAsync(HttpClient client, string path)
    {
        using var content = new StringContent("{}");

        return await client.PostAsync($"{_server.Urls[0]}{path}", content, AbortToken);
    }

    private static ServiceProvider _BuildProvider(Action<IHttpClientBuilder> configureClient)
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("test");
        configureClient(builder);

        return services.BuildServiceProvider();
    }
}
