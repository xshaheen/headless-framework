// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SmsSetupBuilderTests
{
    [Fact]
    public void should_reject_setup_when_default_provider_is_missing()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessSms(static _ => { });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*exactly one default provider*")
            .WithMessage("*UseAwsSns*")
            .WithMessage("*UseCequens*")
            .WithMessage("*UseConnekio*")
            .WithMessage("*UseDev*")
            .WithMessage("*UseInfobip*")
            .WithMessage("*UseNoop*")
            .WithMessage("*UseTwilio*")
            .WithMessage("*UseVictoryLink*")
            .WithMessage("*UseVodafone*");
    }

    [Fact]
    public void throwing_setup_should_leave_the_service_collection_unchanged()
    {
        // given - a collection with unrelated registrations plus a setup that fails the default gate even
        // though it queued a named instance.
        var services = new ServiceCollection();
        services.AddLogging();
        var countBefore = services.Count;

        // when
        var action = () =>
            services.AddHeadlessSms(static setup => setup.AddNamed("audit", static instance => instance.UseNoop()));

        // then - registration is deferred, so nothing touched the collection.
        action.Should().Throw<InvalidOperationException>().WithMessage("*exactly one default provider*");
        services.Should().HaveCount(countBefore);
        services.Should().NotContain(static d => d.ServiceType == typeof(ISmsSender));
        services.Should().NotContain(static d => d.ServiceType == typeof(ISmsSenderProvider));
    }

    [Fact]
    public void should_reject_setup_when_multiple_default_providers_are_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessSms(static setup =>
            {
                setup.UseNoop();
                setup.UseDev("sms.txt");
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple default providers*");
    }

    [Fact]
    public void should_resolve_single_default_sms_sender_when_one_provider_is_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSms(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetServices<ISmsSender>().Should().ContainSingle();
    }

    [Fact]
    public void should_reject_repeated_registration_on_same_service_collection()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSms(static setup => setup.UseNoop());

        // when
        var action = () => services.AddHeadlessSms(static setup => setup.UseNoop());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }

    [Fact]
    public void should_reject_repeated_registration_even_when_first_call_registered_named_instances()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSms(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("sink", static instance => instance.UseDev("out.txt"));
        });

        // when
        var action = () => services.AddHeadlessSms(static setup => setup.UseNoop());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }

    [Fact]
    public void should_register_sms_sender_provider_as_singleton_when_default_is_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSms(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        // then
        var first = provider.GetRequiredService<ISmsSenderProvider>();
        var second = provider.GetRequiredService<ISmsSenderProvider>();
        first.Should().BeSameAs(second);
        first.GetSenderOrNull("anything").Should().BeNull();
    }

    [Fact]
    public async Task named_keyed_registration_should_be_reachable_via_sms_sender_provider()
    {
        // given - exercises the provider-agnostic slot mechanics directly through RegisterProvider.
        var namedSender = Substitute.For<ISmsSender>();
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
        {
            setup.UseNoop();
            setup.AddNamed(
                "marketing",
                instance =>
                {
                    var name = instance.Name;
                    instance.RegisterProvider(svc => svc.AddKeyedSingleton(name, namedSender));
                }
            );
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();

        // then
        senderProvider.GetSender("marketing").Should().BeSameAs(namedSender);
        provider.GetRequiredKeyedService<ISmsSender>("marketing").Should().BeSameAs(namedSender);
        provider.GetRequiredService<ISmsSender>().Should().NotBeSameAs(namedSender);
    }

    [Fact]
    public void add_named_should_reject_zero_providers()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessSms(static setup =>
            {
                setup.UseNoop();
                setup.AddNamed("marketing", static _ => { });
            });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'marketing' requires exactly one provider*UseTwilio*");
    }

    [Fact]
    public void add_named_should_reject_multiple_providers()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessSms(static setup =>
                setup.AddNamed(
                    "marketing",
                    static instance =>
                    {
                        instance.RegisterProvider(static _ => { });
                        instance.RegisterProvider(static _ => { });
                    }
                )
            );

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*'marketing'*");
    }

    [Fact]
    public void add_named_should_reject_whitespace_name()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessSms(static setup =>
                setup.AddNamed(" ", static instance => instance.RegisterProvider(static _ => { }))
            );

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void add_named_should_reject_duplicate_names()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessSms(static setup =>
            {
                setup.UseNoop();
                setup.AddNamed("marketing", static instance => instance.RegisterProvider(static _ => { }));
                setup.AddNamed("marketing", static instance => instance.RegisterProvider(static _ => { }));
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*'marketing'*already configured*");
    }
}
