// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Headless.Hosting.DependencyInjection;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using IdempotencyMiddleware = Headless.Api.Idempotency.IdempotencyMiddleware;

namespace Tests;

public sealed class SetupIdempotencyTests : TestBase
{
    [Fact]
    public void should_register_middleware_with_action_overload()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIdempotency(o => o.Retention = TimeSpan.FromHours(2));

        var descriptor = services.SingleOrDefault(s => s.ServiceType == typeof(IdempotencyMiddleware));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task should_fail_host_start_when_durable_store_is_not_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdempotency(_ => { });

        // when
        var act = () => _RunStartingAsync(services);

        // then — the missing store surfaces at startup with the remedy, not at the first idempotent request
        var exception = (await act.Should().ThrowAsync<MissingRequiredServiceException>()).Which;
        exception.Message.Should().Contain("AddHeadlessIdempotency");
        exception.MissingServices.Should().ContainSingle().Which.ServiceType.Should().Be<IIdempotentOperations>();
    }

    [Fact]
    public async Task should_start_when_durable_store_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdempotency(_ => { });
        services.AddSingleton(Substitute.For<IIdempotentOperations>());

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void should_apply_action_to_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIdempotency(o => o.Retention = TimeSpan.FromHours(7));

        var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        resolved.Retention.Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public void should_default_request_body_buffer_threshold_to_1_mib_plus_1_byte()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdempotency(_ => { });

        var resolved = services.BuildServiceProvider().GetRequiredService<IOptions<IdempotencyOptions>>().Value;

        resolved.RequestBodyBufferThreshold.Should().Be((1 * 1024 * 1024) + 1);
        resolved.MaxBodySizeForHashing.Should().Be(1024 * 1024);
    }

    [Fact]
    public void should_apply_setup_action_with_service_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ConfigSource(TimeSpan.FromHours(9)));

        services.AddIdempotency(
            (o, sp) =>
            {
                var src = sp.GetRequiredService<ConfigSource>();
                o.Retention = src.Expiration;
            }
        );

        var resolved = services.BuildServiceProvider().GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        resolved.Retention.Should().Be(TimeSpan.FromHours(9));
    }

    [Fact]
    public void should_bind_configuration_overload()
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [nameof(IdempotencyOptions.HeaderName)] = "X-My-Key",
            [nameof(IdempotencyOptions.MaxBodySizeForHashing)] = "2048",
            [nameof(IdempotencyOptions.RequestBodyBufferThreshold)] = "1024",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdempotency(config);

        var resolved = services.BuildServiceProvider().GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        resolved.HeaderName.Should().Be("X-My-Key");
        resolved.MaxBodySizeForHashing.Should().Be(2048);
        resolved.RequestBodyBufferThreshold.Should().Be(1024);
    }

    [Fact]
    public void should_leave_cache_predicate_null_when_consumer_left_it_null()
    {
        // Options are a pure value — no DI-time mutation that injects a default. The middleware
        // falls back to DefaultCachePredicate.Instance at request time when this is null.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdempotency(_ => { });

        var resolved = services.BuildServiceProvider().GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        resolved.ShouldCacheResponse.Should().BeNull();
    }

    [Fact]
    public void should_preserve_consumer_cache_predicate()
    {
        Func<Microsoft.AspNetCore.Http.HttpContext, bool> custom = _ => false;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdempotency(o => o.ShouldCacheResponse = custom);

        var resolved = services.BuildServiceProvider().GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        resolved.ShouldCacheResponse.Should().BeSameAs(custom);
    }

    private static async Task _RunStartingAsync(IServiceCollection services)
    {
        // The requirement check ships as an internal IHostedLifecycleService, so drive it the way the host does.
        await using var provider = services.BuildServiceProvider();

        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(AbortToken);
        }
    }

    private sealed record ConfigSource(TimeSpan Expiration);
}
