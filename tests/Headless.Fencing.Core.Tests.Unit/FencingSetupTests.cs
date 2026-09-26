// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class FencingSetupTests : TestBase
{
    [Fact]
    public void should_name_the_provider_calls_when_no_provider_is_chosen()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessFencing(static _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*`UsePostgreSql`, or `UseSqlServer`*");
    }

    [Fact]
    public void should_refuse_two_providers()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessFencing(setup =>
            {
                setup.RegisterExtension(new FakeProvider());
                setup.RegisterExtension(new FakeProvider());
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple storage providers*");
    }

    [Fact]
    public void should_refuse_a_second_registration()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessFencing(static setup => setup.RegisterExtension(new FakeProvider()));

        // when
        var act = () => services.AddHeadlessFencing(static setup => setup.RegisterExtension(new FakeProvider()));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple storage providers*");
    }

    [Fact]
    public void should_register_the_autonomous_leases_and_the_unit_feature_as_singletons()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessFencing(static setup => setup.RegisterExtension(new FakeProvider()));

        // then — a unit-of-work feature must be a singleton, or GetFeature refuses it
        services
            .Should()
            .ContainSingle(d => d.ServiceType == typeof(IUnitOfWorkLeases))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        services
            .Should()
            .ContainSingle(d => d.ServiceType == typeof(IFencedLeases))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task should_follow_a_tenant_change_without_other_tenancy_registration()
    {
        // given
        var store = Substitute.For<ILeaseStore>();
        var services = new ServiceCollection();
        services.AddHeadlessFencing(setup => setup.RegisterExtension(new FakeProvider(store)));
        await using var provider = services.BuildServiceProvider();
        var leases = provider.GetRequiredService<IFencedLeases>();
        var tenant = provider.GetRequiredService<ICurrentTenant>();

        // when
        using (tenant.Change("t1"))
        {
            await leases.GrantAsync("job", "order-1", TimeSpan.FromSeconds(5), AbortToken);
        }

        // then
        await store.Received(1).GrantAsync(new LeaseKey("t1", "job", "order-1"), TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public void should_default_the_duration_bounds_to_one_second_and_one_day()
    {
        var options = _Options(static _ => { });

        options.Value.MinimumLeaseDuration.Should().Be(TimeSpan.FromSeconds(1));
        options.Value.MaximumLeaseDuration.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void should_apply_option_configurations_in_call_order()
    {
        var options = _Options(setup =>
        {
            setup.ConfigureOptions(o => o.MaximumLeaseDuration = TimeSpan.FromHours(1));
            setup.ConfigureOptions(o => o.MaximumLeaseDuration = TimeSpan.FromHours(2));
        });

        options.Value.MaximumLeaseDuration.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void should_reject_a_minimum_duration_that_is_not_positive()
    {
        var options = _Options(setup => setup.ConfigureOptions(o => o.MinimumLeaseDuration = TimeSpan.Zero));

        options.Invoking(x => x.Value).Should().Throw<OptionsValidationException>().WithMessage("*Minimum*");
    }

    [Fact]
    public void should_reject_a_maximum_duration_below_the_minimum()
    {
        var options = _Options(setup =>
            setup.ConfigureOptions(o =>
            {
                o.MinimumLeaseDuration = TimeSpan.FromMinutes(5);
                o.MaximumLeaseDuration = TimeSpan.FromMinutes(1);
            })
        );

        options.Invoking(x => x.Value).Should().Throw<OptionsValidationException>().WithMessage("*Maximum*");
    }

    [Fact]
    public void should_default_the_storage_schema_to_fencing()
    {
        var services = new ServiceCollection();
        services.AddHeadlessFencing(static setup => setup.RegisterExtension(new FakeProvider()));

        var storage = services.BuildServiceProvider().GetRequiredService<IOptions<FencingStorageOptions>>();

        storage.Value.Schema.Should().Be("fencing");
    }

    [Fact]
    public void should_apply_the_storage_schema_from_a_delegate_or_configuration()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Schema"] = "leases" })
            .Build();

        // when
        var fromDelegate = _Storage(setup => setup.ConfigureStorage(o => o.Schema = "custom"));
        var fromConfiguration = _Storage(setup => setup.ConfigureStorage(configuration));

        // then
        fromDelegate.Schema.Should().Be("custom");
        fromConfiguration.Schema.Should().Be("leases");
    }

    private static FencingStorageOptions _Storage(Action<HeadlessFencingSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessFencing(setup =>
        {
            setup.RegisterExtension(new FakeProvider());
            configure(setup);
        });

        return services.BuildServiceProvider().GetRequiredService<IOptions<FencingStorageOptions>>().Value;
    }

    private static IOptions<FencingOptions> _Options(Action<HeadlessFencingSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessFencing(setup =>
        {
            setup.RegisterExtension(new FakeProvider());
            configure(setup);
        });

        return services.BuildServiceProvider().GetRequiredService<IOptions<FencingOptions>>();
    }

    private sealed class FakeProvider(ILeaseStore? store = null) : IFencingProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(store ?? Substitute.For<ILeaseStore>());
        }
    }
}
