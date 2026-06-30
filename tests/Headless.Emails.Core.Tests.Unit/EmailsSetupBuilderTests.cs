// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class EmailsSetupBuilderTests
{
    [Fact]
    public void should_reject_setup_when_default_provider_is_missing()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessEmails(static _ => { });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*exactly one default provider*")
            .And.Contain("UseAzure")
            .And.Contain("UseAwsSes")
            .And.Contain("UseMailkit")
            .And.Contain("UseDevelopment")
            .And.Contain("UseNoop");
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
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple default providers*");
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
    public async Task named_keyed_registration_should_be_reachable_via_email_sender_provider()
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
    public void add_named_should_reject_zero_providers()
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
    public void add_named_should_reject_multiple_providers()
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
    public void add_named_should_reject_whitespace_name()
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
    public void add_named_should_reject_duplicate_names()
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
