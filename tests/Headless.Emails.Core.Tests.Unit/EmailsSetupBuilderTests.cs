// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class EmailsSetupBuilderTests
{
    [Fact]
    public void should_reject_setup_when_provider_is_missing()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessEmails(static _ => { });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("exactly one provider")
            .And.Contain("UseAzure")
            .And.Contain("UseAwsSes")
            .And.Contain("UseMailkit")
            .And.Contain("UseDevelopment")
            .And.Contain("UseNoop");
    }

    [Fact]
    public void should_reject_setup_when_multiple_providers_are_configured()
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
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }

    [Fact]
    public void should_resolve_single_email_sender_when_one_provider_is_configured()
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
    public void should_reject_repeated_provider_registration_on_same_service_collection()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessEmails(static setup => setup.UseNoop());

        // when
        var action = () => services.AddHeadlessEmails(static setup => setup.UseNoop());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }
}
