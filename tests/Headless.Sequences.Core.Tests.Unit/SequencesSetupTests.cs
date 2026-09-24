// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Sequences;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SequencesSetupTests : TestBase
{
    [Fact]
    public void should_name_the_provider_calls_when_no_provider_is_chosen()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessSequences(static _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*`UsePostgreSql`, or `UseSqlServer`*");
    }

    [Fact]
    public void should_refuse_two_providers()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessSequences(setup =>
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
        services.AddHeadlessSequences(static setup => setup.RegisterExtension(new FakeProvider()));

        // when
        var act = () => services.AddHeadlessSequences(static setup => setup.RegisterExtension(new FakeProvider()));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple storage providers*");
    }

    [Fact]
    public void should_register_the_generator_and_the_unit_feature_as_singletons()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSequences(static setup => setup.RegisterExtension(new FakeProvider()));

        // then — a unit-of-work feature must be a singleton, or GetFeature refuses it
        services
            .Should()
            .ContainSingle(d => d.ServiceType == typeof(IUnitOfWorkSequences))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        services
            .Should()
            .ContainSingle(d => d.ServiceType == typeof(ISequenceGenerator))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task should_follow_a_tenant_change_without_other_tenancy_registration()
    {
        // given
        var store = Substitute.For<ISequenceStore>();
        var services = new ServiceCollection();
        services.AddHeadlessSequences(setup => setup.RegisterExtension(new FakeProvider(store)));
        await using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ISequenceGenerator>();
        var tenant = provider.GetRequiredService<ICurrentTenant>();

        // when
        using (tenant.Change("t1"))
        {
            await generator.NextAsync("receipt", cancellationToken: AbortToken);
        }

        // then
        await store.Received(1).IncrementAsync(new SequenceKey("t1", "receipt", ""), 1, 1, AbortToken);
    }

    [Fact]
    public async Task should_apply_builder_policies_in_call_order()
    {
        // given
        var store = Substitute.For<ISequenceStore>();
        var services = new ServiceCollection();
        services.AddHeadlessSequences(setup =>
        {
            setup.RegisterExtension(new FakeProvider(store));
            setup.DefaultPolicy(new SequencePolicy { Start = 5 });
            setup.Policy("invoice", new SequencePolicy { Step = 2 });
            setup.Policy("invoice", new SequencePolicy { Step = 3 });
        });
        await using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ISequenceGenerator>();

        // when
        await generator.NextAsync("receipt", cancellationToken: AbortToken);
        await generator.NextAsync("invoice", cancellationToken: AbortToken);

        // then
        await store.Received(1).IncrementAsync(new SequenceKey("", "receipt", ""), 5, 1, AbortToken);
        await store.Received(1).IncrementAsync(new SequenceKey("", "invoice", ""), 1, 3, AbortToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_default_policy_step_that_is_not_positive(long step)
    {
        var options = _Options(setup => setup.DefaultPolicy(new SequencePolicy { Step = step }));

        options.Invoking(x => x.Value).Should().Throw<OptionsValidationException>().WithMessage("*Step*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_registered_policy_step_that_is_not_positive(long step)
    {
        var options = _Options(setup => setup.Policy("invoice", new SequencePolicy { Step = step }));

        options.Invoking(x => x.Value).Should().Throw<OptionsValidationException>().WithMessage("*Step*");
    }

    [Fact]
    public void should_reject_an_over_length_name_added_through_options()
    {
        var name = new string('n', SequenceFieldLimits.NameMaxLength + 1);

        var options = _Options(setup => setup.ConfigureOptions(o => o.Policies[name] = new SequencePolicy()));

        options.Invoking(x => x.Value).Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_accept_valid_policies()
    {
        var options = _Options(setup =>
            setup.Policy(
                "invoice",
                new SequencePolicy
                {
                    Start = 0,
                    Step = 5,
                    Mode = SequenceMode.GapFree,
                }
            )
        );

        options.Value.Policies["invoice"].Mode.Should().Be(SequenceMode.GapFree);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void should_refuse_a_blank_policy_name_at_the_builder(string name)
    {
        var act = () =>
            new ServiceCollection().AddHeadlessSequences(setup =>
            {
                setup.RegisterExtension(new FakeProvider());
                setup.Policy(name, new SequencePolicy());
            });

        act.Should().Throw<ArgumentException>();
    }

    private static IOptions<SequencesOptions> _Options(Action<HeadlessSequencesSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessSequences(setup =>
        {
            setup.RegisterExtension(new FakeProvider());
            configure(setup);
        });

        return services.BuildServiceProvider().GetRequiredService<IOptions<SequencesOptions>>();
    }

    private sealed class FakeProvider(ISequenceStore? store = null) : ISequencesProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(store ?? Substitute.For<ISequenceStore>());
        }
    }
}
