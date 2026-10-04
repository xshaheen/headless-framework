// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;
using Fixture = Headless.Messaging.GeneratedModuleFixture;

namespace Tests.Registration;

/// <summary>
/// Covers the durable deployment settings a host tunes onto a declared consumer by identity: its inbox retention and its
/// circuit-breaker overrides, from <c>Tune</c> and from <c>Headless:Messaging:Consumers:{identity}</c> configuration.
/// </summary>
public sealed class ConsumerTuningDurableSettingsTests : TestBase
{
    private const string _Ledger = Fixture.LedgerProjection.Identity;

    [Fact]
    public void should_apply_a_tuned_inbox_retention_to_every_message_of_the_identity()
    {
        // given
        using var provider = _BuildProvider(setup =>
            setup.Tune(_Ledger, consumer => consumer.InboxRetention(TimeSpan.FromDays(7)))
        );

        // when
        var ledger = _LedgerConsumers(provider);
        var descriptors = provider
            .GetRequiredService<IConsumerServiceSelector>()
            .SelectCandidates()
            .Where(x => string.Equals(x.ConsumerIdentity, _Ledger, StringComparison.Ordinal))
            .ToList();

        // then
        ledger.Should().HaveCount(2).And.OnlyContain(x => x.InboxRetention == TimeSpan.FromDays(7));
        descriptors.Should().HaveCount(2).And.OnlyContain(x => x.InboxRetention == TimeSpan.FromDays(7));
    }

    [Fact]
    public void should_keep_the_default_inbox_retention_when_no_tuning_sets_one()
    {
        // given
        using var provider = _BuildProvider();

        // when
        var ledger = _LedgerConsumers(provider);

        // then
        ledger.Should().OnlyContain(x => x.InboxRetention == TimeSpan.FromDays(30));
    }

    [Fact]
    public void should_register_one_circuit_breaker_override_for_an_identity_covering_several_messages()
    {
        // given
        using var provider = _BuildProvider(setup =>
            setup.Tune(
                _Ledger,
                consumer =>
                    consumer.CircuitBreaker(options =>
                    {
                        options.FailureThreshold = 3;
                        options.OpenDuration = TimeSpan.FromSeconds(45);
                    })
            )
        );

        // when
        _LedgerConsumers(provider);
        var found = provider
            .GetRequiredService<ConsumerCircuitBreakerRegistry>()
            .TryGet(CircuitBreakerKeys.For(MessageLane.Bus, _Ledger), out var options);

        // then
        found.Should().BeTrue();
        options!.FailureThreshold.Should().Be(3);
        options.OpenDuration.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void should_let_a_later_tuning_replace_an_earlier_circuit_breaker_override()
    {
        // given
        using var provider = _BuildProvider(setup =>
        {
            setup.Tune(_Ledger, consumer => consumer.CircuitBreaker(options => options.FailureThreshold = 3));
            setup.Tune(_Ledger, consumer => consumer.CircuitBreaker(options => options.Enabled = false));
        });

        // when
        _LedgerConsumers(provider);
        provider
            .GetRequiredService<ConsumerCircuitBreakerRegistry>()
            .TryGet(CircuitBreakerKeys.For(MessageLane.Bus, _Ledger), out var options);

        // then
        options!.Enabled.Should().BeFalse();
        options.FailureThreshold.Should().BeNull();
    }

    [Fact]
    public void should_bind_inbox_retention_and_circuit_breaker_from_configuration_after_tune()
    {
        // given
        using var provider = _BuildProvider(
            setup => setup.Tune(_Ledger, consumer => consumer.InboxRetention(TimeSpan.FromDays(7))),
            services =>
                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(
                            new Dictionary<string, string?>(StringComparer.Ordinal)
                            {
                                [$"Headless:Messaging:Consumers:{_Ledger}:InboxRetention"] = "2.00:00:00",
                                [$"Headless:Messaging:Consumers:{_Ledger}:CircuitBreaker:Enabled"] = "false",
                                [$"Headless:Messaging:Consumers:{_Ledger}:CircuitBreaker:FailureThreshold"] = "4",
                                [$"Headless:Messaging:Consumers:{_Ledger}:CircuitBreaker:OpenDuration"] = "00:01:00",
                            }
                        )
                        .Build()
                )
        );

        // when
        var ledger = _LedgerConsumers(provider);
        provider
            .GetRequiredService<ConsumerCircuitBreakerRegistry>()
            .TryGet(CircuitBreakerKeys.For(MessageLane.Bus, _Ledger), out var options);

        // then
        ledger.Should().OnlyContain(x => x.InboxRetention == TimeSpan.FromDays(2));
        options.Should().NotBeNull();
        options!.Enabled.Should().BeFalse();
        options.FailureThreshold.Should().Be(4);
        options.OpenDuration.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void should_reject_an_inbox_retention_that_is_not_a_positive_whole_second(int milliseconds)
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.ConfigureMessaging(messaging =>
                messaging.Tune(_Ledger, consumer => consumer.InboxRetention(TimeSpan.FromMilliseconds(milliseconds)))
            );

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*whole-second*");
    }

    [Fact]
    public void should_reject_zero_concurrency()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.ConfigureMessaging(messaging => messaging.Tune(_Ledger, consumer => consumer.Concurrency(0)));

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static List<ConsumerMetadata> _LedgerConsumers(IServiceProvider provider) =>
        [
            .. provider
                .GetRequiredService<ConsumerRegistry>()
                .GetAll()
                .Where(x => string.Equals(x.ConsumerIdentity, _Ledger, StringComparison.Ordinal)),
        ];

    private static ServiceProvider _BuildProvider(
        Action<Headless.Messaging.MessagingSetupBuilder>? configure = null,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Fixture.FixtureProbe>();
        configureServices?.Invoke(services);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            setup.AddModule<Fixture.MessagingModule>();
            configure?.Invoke(setup);
        });

        return services.BuildServiceProvider();
    }
}
