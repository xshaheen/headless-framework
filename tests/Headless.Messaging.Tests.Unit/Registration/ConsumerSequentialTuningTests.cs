// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Headless.Messaging.GeneratedModuleFixture;

namespace Tests.Registration;

/// <summary>
/// Covers per-key sequencing a host tunes onto a declared consumer by identity, from <c>Tune</c> and from
/// <c>Headless:Messaging:Consumers:{identity}:Sequential</c> configuration.
/// </summary>
public sealed class ConsumerSequentialTuningTests : TestBase
{
    private const string _Ledger = Fixture.LedgerProjection.Identity;
    private const string _ConfigurationKey = $"Headless:Messaging:Consumers:{_Ledger}:Sequential";

    [Fact]
    public void should_leave_an_untuned_consumer_unsequenced()
    {
        // given
        using var provider = _BuildProvider();

        // when
        var consumers = provider.GetRequiredService<ConsumerRegistry>().GetAll();

        // then
        consumers.Should().OnlyContain(x => !x.Sequential);
    }

    [Fact]
    public void should_make_every_message_of_the_tuned_identity_sequential()
    {
        // given
        using var provider = _BuildProvider(setup =>
            setup.Tune(_Ledger, consumer => consumer.Concurrency(8).Sequential())
        );

        // when
        var ledger = _LedgerConsumers(provider);
        var descriptors = provider
            .GetRequiredService<IConsumerServiceSelector>()
            .SelectCandidates()
            .Where(x => string.Equals(x.ConsumerIdentity, _Ledger, StringComparison.Ordinal))
            .ToList();

        // then
        ledger.Should().HaveCount(2).And.OnlyContain(x => x.Sequential && x.Concurrency == 8);
        descriptors.Should().HaveCount(2).And.OnlyContain(x => x.Sequential && x.Concurrency == 8);
    }

    [Fact]
    public void should_let_a_later_tuning_turn_sequencing_off()
    {
        // given
        using var provider = _BuildProvider(setup =>
        {
            setup.Tune(_Ledger, consumer => consumer.Sequential());
            setup.Tune(_Ledger, consumer => consumer.Concurrency(2));
            setup.Tune(_Ledger, consumer => consumer.Sequential(enabled: false));
        });

        // when
        var ledger = _LedgerConsumers(provider);

        // then
        ledger.Should().OnlyContain(x => !x.Sequential && x.Concurrency == 2);
    }

    [Theory]
    [InlineData("true", false, true)]
    [InlineData("True", false, true)]
    [InlineData("false", true, false)]
    public void should_apply_configuration_over_the_tuned_value(string configured, bool tuned, bool expected)
    {
        // given
        using var provider = _BuildProvider(
            setup => setup.Tune(_Ledger, consumer => consumer.Sequential(tuned)),
            _Configuration((_ConfigurationKey, configured))
        );

        // when
        var ledger = _LedgerConsumers(provider);

        // then
        ledger.Should().OnlyContain(x => x.Sequential == expected);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void should_fail_startup_on_a_configured_value_that_is_not_a_boolean(string configured)
    {
        // given
        using var provider = _BuildProvider(configureServices: _Configuration((_ConfigurationKey, configured)));

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Sequential' must be true or false*");
    }

    [Fact]
    public void should_fail_startup_when_tune_makes_an_every_instance_consumer_sequential()
    {
        // given
        using var provider = _BuildProvider(setup =>
            setup.Tune(Fixture.InvoiceProjection.Identity, consumer => consumer.Sequential())
        );

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*Tune gives every-instance consumer '{Fixture.InvoiceProjection.Identity}' per-key sequencing*"
            );
    }

    [Fact]
    public void should_fail_startup_when_configuration_makes_an_every_instance_consumer_sequential()
    {
        // given
        using var provider = _BuildProvider(
            configureServices: _Configuration(
                ($"Headless:Messaging:Consumers:{Fixture.InvoiceProjection.Identity}:Sequential", "true")
            )
        );

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*gives every-instance consumer '{Fixture.InvoiceProjection.Identity}' per-key sequencing*");
    }

    [Fact]
    public void should_accept_turning_sequencing_off_on_an_every_instance_consumer()
    {
        // given
        using var provider = _BuildProvider(setup =>
            setup.Tune(Fixture.InvoiceProjection.Identity, consumer => consumer.Sequential(enabled: false))
        );

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should().NotThrow();
    }

    private static Action<IServiceCollection> _Configuration(params (string Key, string Value)[] entries) =>
        services =>
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(entries.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
                    .Build()
            );

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
