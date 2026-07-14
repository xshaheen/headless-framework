// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Headless.DistributedLocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IdempotencyMiddleware = Headless.Api.Idempotency.IdempotencyMiddleware;

namespace Tests;

public sealed class SetupIdempotencyTests
{
    [Fact]
    public void should_register_middleware_with_action_overload()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIdempotency(o => o.IdempotencyKeyExpiration = TimeSpan.FromHours(2));

        var descriptor = services.SingleOrDefault(s => s.ServiceType == typeof(IdempotencyMiddleware));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void should_register_di_validator()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIdempotency(_ => { });

        services
            .Should()
            .Contain(s =>
                s.ServiceType == typeof(IValidateOptions<IdempotencyOptions>)
                && s.ImplementationType == typeof(IdempotencyOptionsDiValidator)
            );
    }

    [Fact]
    public void should_apply_action_to_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIdempotency(o => o.IdempotencyKeyExpiration = TimeSpan.FromHours(7));

        var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        resolved.IdempotencyKeyExpiration.Should().Be(TimeSpan.FromHours(7));
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
                o.IdempotencyKeyExpiration = src.Expiration;
            }
        );

        var resolved = services.BuildServiceProvider().GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        resolved.IdempotencyKeyExpiration.Should().Be(TimeSpan.FromHours(9));
    }

    [Fact]
    public void should_bind_configuration_overload()
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [nameof(IdempotencyOptions.HeaderName)] = "X-My-Key",
            [nameof(IdempotencyOptions.MaxBodySizeForHashing)] = "2048",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdempotency(config);

        var resolved = services.BuildServiceProvider().GetRequiredService<IOptions<IdempotencyOptions>>().Value;
        resolved.HeaderName.Should().Be("X-My-Key");
        resolved.MaxBodySizeForHashing.Should().Be(2048);
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

    [Fact]
    public void di_validator_should_pass_for_reject_strategy_without_lock_provider()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var validator = new IdempotencyOptionsDiValidator(sp);

        var result = validator.Validate(
            name: null,
            new IdempotencyOptions { InFlightStrategy = InFlightStrategy.Reject }
        );

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void di_validator_should_pass_for_wait_and_replay_with_lock_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLock>());
        var sp = services.BuildServiceProvider();
        var validator = new IdempotencyOptionsDiValidator(sp);

        var result = validator.Validate(
            name: null,
            new IdempotencyOptions { InFlightStrategy = InFlightStrategy.WaitAndReplay }
        );

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void di_validator_should_fail_for_wait_and_replay_without_lock_provider()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var validator = new IdempotencyOptionsDiValidator(sp);

        var result = validator.Validate(
            name: null,
            new IdempotencyOptions { InFlightStrategy = InFlightStrategy.WaitAndReplay }
        );

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(IDistributedLock));
        result.FailureMessage.Should().Contain(nameof(InFlightStrategy.WaitAndReplay));
    }

    private sealed record ConfigSource(TimeSpan Expiration);
}
