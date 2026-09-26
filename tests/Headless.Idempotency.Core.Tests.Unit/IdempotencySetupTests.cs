// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class IdempotencySetupTests : TestBase
{
    [Fact]
    public void should_name_the_provider_calls_when_no_provider_is_chosen()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessIdempotency(static _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*`UsePostgreSql`, or `UseSqlServer`*");
    }

    [Fact]
    public void should_refuse_two_providers()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessIdempotency(setup =>
            {
                setup.RegisterExtension(new FakeProvider());
                setup.RegisterExtension(new FakeProvider());
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple storage providers*");
    }

    [Fact]
    public void should_register_the_autonomous_operations_the_unit_feature_and_the_retention_service()
    {
        // given - no other Headless feature is registered first
        var services = new ServiceCollection();

        // when
        services.AddHeadlessIdempotency(static setup => setup.RegisterExtension(new FakeProvider()));

        // then — a unit-of-work feature must be a singleton, or GetFeature refuses it
        services
            .Should()
            .ContainSingle(d => d.ServiceType == typeof(IUnitOfWorkIdempotency))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        services
            .Should()
            .ContainSingle(d => d.ServiceType == typeof(IIdempotentOperations))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        services.Should().Contain(d => d.ServiceType == typeof(IHostedService));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IIdempotentOperations>().Should().NotBeNull();
        provider.GetRequiredService<IUnitOfWorkIdempotency>().Should().NotBeNull();

        // Records are keyed by the current tenant, so it must follow ICurrentTenant.Change rather than stay on the
        // host scope.
        var tenant = provider.GetRequiredService<ICurrentTenant>();
        using (tenant.Change("t1"))
        {
            tenant.Id.Should().Be("t1");
        }
    }

    [Fact]
    public void should_default_retention_lease_and_purge_settings()
    {
        var options = _Options(static _ => { }).Value;

        options.DefaultRetention.Should().Be(TimeSpan.FromHours(24));
        options.DefaultLeaseDuration.Should().Be(TimeSpan.FromMinutes(2));
        options.MinimumLeaseDuration.Should().Be(TimeSpan.FromSeconds(1));
        options.MaximumLeaseDuration.Should().Be(TimeSpan.FromDays(1));
        options.PurgeInterval.Should().Be(TimeSpan.FromHours(1));
        options.PurgeBatchSize.Should().Be(1000);
    }

    [Fact]
    public void should_apply_option_configurations_in_call_order_and_allow_disabling_the_purge()
    {
        var options = _Options(setup =>
        {
            setup.ConfigureOptions(o => o.DefaultRetention = TimeSpan.FromDays(1));
            setup.ConfigureOptions(o =>
            {
                o.DefaultRetention = TimeSpan.FromDays(7);
                o.PurgeInterval = null;
            });
        }).Value;

        options.DefaultRetention.Should().Be(TimeSpan.FromDays(7));
        options.PurgeInterval.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(IdempotentOperationsOptions.DefaultRetention))]
    [InlineData(nameof(IdempotentOperationsOptions.DefaultLeaseDuration))]
    [InlineData(nameof(IdempotentOperationsOptions.PurgeInterval))]
    [InlineData(nameof(IdempotentOperationsOptions.PurgeBatchSize))]
    public void should_reject_a_setting_that_is_not_positive(string setting)
    {
        var options = _Options(setup =>
            setup.ConfigureOptions(o =>
            {
                switch (setting)
                {
                    case nameof(IdempotentOperationsOptions.DefaultRetention):
                        o.DefaultRetention = TimeSpan.Zero;
                        break;
                    case nameof(IdempotentOperationsOptions.DefaultLeaseDuration):
                        o.DefaultLeaseDuration = TimeSpan.Zero;
                        break;
                    case nameof(IdempotentOperationsOptions.PurgeInterval):
                        o.PurgeInterval = TimeSpan.Zero;
                        break;
                    default:
                        o.PurgeBatchSize = 0;
                        break;
                }
            })
        );

        options.Invoking(x => x.Value).Should().Throw<OptionsValidationException>().WithMessage($"*{setting}*");
    }

    [Fact]
    public void should_reject_a_default_lease_duration_outside_the_bounds()
    {
        var options = _Options(setup => setup.ConfigureOptions(o => o.DefaultLeaseDuration = TimeSpan.FromDays(2)));

        options
            .Invoking(x => x.Value)
            .Should()
            .Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(IdempotentOperationsOptions.DefaultLeaseDuration)}*");
    }

    [Fact]
    public void should_default_the_storage_schema_and_apply_a_delegate_or_configuration()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Schema"] = "keys" })
            .Build();

        // when
        var byDefault = _Storage(static _ => { });
        var fromDelegate = _Storage(setup => setup.ConfigureStorage(o => o.Schema = "custom"));
        var fromConfiguration = _Storage(setup => setup.ConfigureStorage(configuration));

        // then
        byDefault.Schema.Should().Be("idempotency");
        fromDelegate.Schema.Should().Be("custom");
        fromConfiguration.Schema.Should().Be("keys");
    }

    private static IdempotencyStorageOptions _Storage(Action<HeadlessIdempotencySetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessIdempotency(setup =>
        {
            setup.RegisterExtension(new FakeProvider());
            configure(setup);
        });

        return services.BuildServiceProvider().GetRequiredService<IOptions<IdempotencyStorageOptions>>().Value;
    }

    private static IOptions<IdempotentOperationsOptions> _Options(Action<HeadlessIdempotencySetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessIdempotency(setup =>
        {
            setup.RegisterExtension(new FakeProvider());
            configure(setup);
        });

        return services.BuildServiceProvider().GetRequiredService<IOptions<IdempotentOperationsOptions>>();
    }

    private sealed class FakeProvider : IIdempotencyProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(Substitute.For<IIdempotencyRecordStore>());
        }
    }
}
