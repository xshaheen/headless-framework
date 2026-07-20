// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class EmailsSetupBuilderTests
{
    [Fact]
    public void should_allow_setup_with_no_default_and_register_provider()
    {
        // given - the default slot is optional (at most one); an empty setup still registers the factory.
        var services = new ServiceCollection();

        // when
        services.AddHeadlessEmails(static _ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<IEmailSender>().Should().BeNull();
        provider.GetRequiredService<IEmailSenderProvider>().RegisteredNames.Should().BeEmpty();
    }

    [Fact]
    public void should_not_register_default_email_sender_when_named_only_setup()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessEmails(static setup =>
            setup.AddNamed("sink", static instance => instance.UseDevelopment("out.txt"))
        );
        using var provider = services.BuildServiceProvider();

        // then - the named instance resolves while the unkeyed default stays unregistered.
        provider.GetService<IEmailSender>().Should().BeNull();
        provider.GetRequiredService<IEmailSenderProvider>().GetSender("sink").Should().BeOfType<DevEmailSender>();
    }

    [Fact]
    public void should_reject_setup_when_multiple_default_providers_are_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessEmails(static setup =>
            {
                setup.UseNoop();
                setup.UseDevelopment("emails.txt");
            });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*at most one default*")
            .WithMessage("*AddNamed*");
    }

    [Fact]
    public void should_resolve_single_default_email_sender_when_one_provider_is_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessEmails(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetServices<IEmailSender>().Should().ContainSingle().Which.Should().BeOfType<NoopEmailSender>();
    }

    [Fact]
    public void should_reject_repeated_registration_on_same_service_collection()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessEmails(static setup => setup.UseNoop());

        // when
        var action = () => services.AddHeadlessEmails(static setup => setup.UseNoop());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }

    [Fact]
    public void should_reject_repeated_registration_even_when_first_call_registered_named_instances()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessEmails(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("sink", static instance => instance.UseDevelopment("out.txt"));
        });

        // when
        var action = () => services.AddHeadlessEmails(static setup => setup.UseNoop());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }

    [Fact]
    public void should_register_email_sender_provider_as_singleton_when_default_is_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessEmails(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        // then
        var first = provider.GetRequiredService<IEmailSenderProvider>();
        var second = provider.GetRequiredService<IEmailSenderProvider>();
        first.Should().BeSameAs(second);
        first.GetSenderOrNull("anything").Should().BeNull();
    }

    [Fact]
    public async Task should_be_reachable_via_email_sender_provider_when_named_keyed_registration()
    {
        // given - the instance-scoped Use* overloads land per provider (U2-U5); this exercises the
        // provider-agnostic slot mechanics directly through RegisterProvider, mirroring Caching.
        var namedSender = Substitute.For<IEmailSender>();
        var services = new ServiceCollection();
        services.AddHeadlessEmails(setup =>
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
        var senderProvider = provider.GetRequiredService<IEmailSenderProvider>();

        // then
        senderProvider.GetSender("marketing").Should().BeSameAs(namedSender);
        provider.GetRequiredKeyedService<IEmailSender>("marketing").Should().BeSameAs(namedSender);
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NoopEmailSender>();
    }

    [Fact]
    public void should_reject_zero_providers_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessEmails(static setup => setup.AddNamed("marketing", static _ => { }));

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'marketing' requires exactly one provider*UseAzure*");
    }

    [Fact]
    public void should_reject_multiple_providers_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessEmails(static setup =>
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
    public void should_reject_whitespace_name_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessEmails(static setup =>
                setup.AddNamed(" ", static instance => instance.RegisterProvider(static _ => { }))
            );

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_duplicate_names_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessEmails(static setup =>
            {
                setup.UseNoop();
                setup.AddNamed("marketing", static instance => instance.RegisterProvider(static _ => { }));
                setup.AddNamed("marketing", static instance => instance.RegisterProvider(static _ => { }));
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*'marketing'*already configured*");
    }
}
