// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.RateLimiting;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Headless.Messaging.GeneratedModuleFixture;

namespace Tests.Registration;

/// <summary>
/// Covers the rate limit a host tunes onto a declared consumer by identity, from <c>Tune</c> and from
/// <c>Headless:Messaging:Consumers:{identity}:RateLimit</c> configuration.
/// </summary>
public sealed class ConsumerRateLimitTuningTests : TestBase
{
    private const string _Ledger = Fixture.LedgerProjection.Identity;
    private const string _ConfigurationPrefix = $"Headless:Messaging:Consumers:{_Ledger}:RateLimit";

    public static TheoryData<int, int> InvalidFixedWindows =>
        new()
        {
            { 0, 1_000 },
            { -1, 1_000 },
            { 1, 0 },
            { 1, -1_000 },
            { 1, 86_400_001 },
        };

    public static TheoryData<int, int, int> InvalidTokenBuckets =>
        new()
        {
            { 0, 1, 1_000 },
            { 1, 0, 1_000 },
            { 1, 1, 0 },
            { 1, 1, 86_400_001 },
        };

    [Theory]
    [MemberData(nameof(InvalidFixedWindows))]
    public void should_reject_a_fixed_window_without_positive_permits_and_a_window_up_to_a_day(
        int permitLimit,
        int windowMilliseconds
    )
    {
        // when
        var act = () => ConsumerRateLimit.FixedWindow(permitLimit, TimeSpan.FromMilliseconds(windowMilliseconds));

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [MemberData(nameof(InvalidTokenBuckets))]
    public void should_reject_a_token_bucket_without_positive_tokens_and_a_period_up_to_a_day(
        int tokenLimit,
        int tokensPerPeriod,
        int periodMilliseconds
    )
    {
        // when
        var act = () =>
            ConsumerRateLimit.TokenBucket(tokenLimit, tokensPerPeriod, TimeSpan.FromMilliseconds(periodMilliseconds));

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_accept_a_window_of_exactly_one_day()
    {
        // when
        var act = () => ConsumerRateLimit.FixedWindow(10_000, ConsumerRateLimit.MaxPeriod);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_reject_a_null_rate_limit()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.ConfigureMessaging(messaging => messaging.Tune(_Ledger, consumer => consumer.RateLimit(null!)));

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_apply_one_tuned_rate_limit_to_every_message_of_the_identity()
    {
        // given
        var rateLimit = ConsumerRateLimit.FixedWindow(10, TimeSpan.FromSeconds(1));
        using var provider = _BuildProvider(setup => setup.Tune(_Ledger, consumer => consumer.RateLimit(rateLimit)));

        // when
        var ledger = _LedgerConsumers(provider);
        var descriptors = provider
            .GetRequiredService<IConsumerServiceSelector>()
            .SelectCandidates()
            .Where(x => string.Equals(x.ConsumerIdentity, _Ledger, StringComparison.Ordinal))
            .ToList();

        // then
        ledger.Should().HaveCount(2).And.OnlyContain(x => ReferenceEquals(x.RateLimit, rateLimit));
        descriptors.Should().HaveCount(2).And.OnlyContain(x => ReferenceEquals(x.RateLimit, rateLimit));
    }

    [Fact]
    public void should_leave_an_untuned_consumer_unthrottled()
    {
        // given
        using var provider = _BuildProvider();

        // when
        var consumers = provider.GetRequiredService<ConsumerRegistry>().GetAll();

        // then
        consumers.Should().OnlyContain(x => x.RateLimit == null);
    }

    [Fact]
    public void should_let_a_later_tuning_replace_an_earlier_rate_limit()
    {
        // given
        var later = ConsumerRateLimit.TokenBucket(5, 1, TimeSpan.FromSeconds(1));
        using var provider = _BuildProvider(setup =>
        {
            setup.Tune(
                _Ledger,
                consumer => consumer.RateLimit(ConsumerRateLimit.FixedWindow(1, TimeSpan.FromSeconds(1)))
            );
            setup.Tune(_Ledger, consumer => consumer.Concurrency(2));
            setup.Tune(_Ledger, consumer => consumer.RateLimit(later));
        });

        // when
        var ledger = _LedgerConsumers(provider);

        // then
        ledger.Should().OnlyContain(x => ReferenceEquals(x.RateLimit, later) && x.Concurrency == 2);
    }

    [Fact]
    public void should_accept_a_rate_limit_on_an_every_instance_consumer()
    {
        // given
        var rateLimit = ConsumerRateLimit.FixedWindow(10, TimeSpan.FromSeconds(1));
        using var provider = _BuildProvider(setup =>
            setup.Tune(Fixture.InvoiceProjection.Identity, consumer => consumer.RateLimit(rateLimit))
        );

        // when
        var invoices = provider
            .GetRequiredService<ConsumerRegistry>()
            .GetAll()
            .Where(x => string.Equals(x.ConsumerIdentity, Fixture.InvoiceProjection.Identity, StringComparison.Ordinal))
            .ToList();

        // then
        invoices
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(x => x.EveryInstance && ReferenceEquals(x.RateLimit, rateLimit));
    }

    [Fact]
    public void should_bind_a_fixed_window_from_configuration_over_the_tuned_rate_limit()
    {
        // given
        using var provider = _BuildProvider(
            setup =>
                setup.Tune(
                    _Ledger,
                    consumer => consumer.RateLimit(ConsumerRateLimit.TokenBucket(1, 1, TimeSpan.FromSeconds(1)))
                ),
            _Configuration(
                (_ConfigurationPrefix + ":FixedWindow:PermitLimit", "7"),
                (_ConfigurationPrefix + ":FixedWindow:Window", "00:00:01")
            )
        );

        // when
        var ledger = _LedgerConsumers(provider);
        using var limiter = ledger[0].RateLimit!.CreateLimiter();

        // then
        ledger.Should().OnlyContain(x => ReferenceEquals(x.RateLimit, ledger[0].RateLimit));
        limiter.Should().BeOfType<FixedWindowRateLimiter>();
        limiter.GetStatistics()!.CurrentAvailablePermits.Should().Be(7);
    }

    [Fact]
    public void should_bind_a_token_bucket_from_configuration()
    {
        // given
        using var provider = _BuildProvider(
            configureServices: _Configuration(
                (_ConfigurationPrefix + ":TokenBucket:TokenLimit", "12"),
                (_ConfigurationPrefix + ":TokenBucket:TokensPerPeriod", "3"),
                (_ConfigurationPrefix + ":TokenBucket:ReplenishmentPeriod", "00:00:02")
            )
        );

        // when
        var ledger = _LedgerConsumers(provider);
        using var limiter = ledger[0].RateLimit!.CreateLimiter();

        // then
        limiter.Should().BeOfType<TokenBucketRateLimiter>();
        limiter.GetStatistics()!.CurrentAvailablePermits.Should().Be(12);
    }

    [Theory]
    [InlineData("FixedWindow:PermitLimit=0|FixedWindow:Window=00:00:01", "*FixedWindow' does not describe a valid*")]
    [InlineData("FixedWindow:PermitLimit=ten|FixedWindow:Window=00:00:01", "*PermitLimit' must be an integer*")]
    [InlineData("FixedWindow:PermitLimit=1|FixedWindow:Window=soon", "*Window' must be a duration*")]
    [InlineData("FixedWindow:PermitLimit=1", "*Window' must be a duration*")]
    [InlineData("FixedWindow:PermitLimit=1|FixedWindow:Window=2.00:00:00", "*does not describe a valid rate limit*")]
    [InlineData("FixedWindow:PermitLimit=1|FixedWindow:Window=00:00:01|FixedWindow:Burst=2", "*Burst' is not a*")]
    [InlineData("SlidingWindow:PermitLimit=1", "*SlidingWindow' is not a rate limit algorithm*")]
    [InlineData(
        "FixedWindow:PermitLimit=1|FixedWindow:Window=00:00:01|TokenBucket:TokenLimit=1",
        "*must hold exactly one of FixedWindow or TokenBucket*"
    )]
    [InlineData("TokenBucket:TokenLimit=1|TokenBucket:ReplenishmentPeriod=00:00:01", "*TokensPerPeriod' must be*")]
    public void should_fail_startup_on_an_invalid_configured_rate_limit(string settings, string expectedMessage)
    {
        // given
        var entries = settings
            .Split('|')
            .Select(static setting => setting.Split('='))
            .Select(static pair => (_ConfigurationPrefix + ":" + pair[0], pair[1]))
            .ToArray();
        using var provider = _BuildProvider(configureServices: _Configuration(entries));

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
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
