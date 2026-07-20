// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupDevNamedEmailTests
{
    [Fact]
    public void should_resolve_named_development_sender()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("sink", instance => instance.UseDevelopment("out.txt"));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredKeyedService<IEmailSender>("sink").Should().BeOfType<DevEmailSender>();
        provider.GetRequiredService<IEmailSenderProvider>().GetSender("sink").Should().BeOfType<DevEmailSender>();
    }

    [Fact]
    public void should_resolve_named_noop_sender()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseDevelopment("default.txt");
            setup.AddNamed("blackhole", instance => instance.UseNoop());
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredKeyedService<IEmailSender>("blackhole").Should().BeOfType<NoopEmailSender>();
        provider.GetRequiredService<IEmailSenderProvider>().GetSender("blackhole").Should().BeOfType<NoopEmailSender>();
    }

    [Fact]
    public void should_resolve_default_and_named_dev_senders_independently()
    {
        // given
        var services = new ServiceCollection();

        // when - default Noop plus a named development sink.
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("sink", instance => instance.UseDevelopment("out.txt"));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NoopEmailSender>();
        provider.GetRequiredKeyedService<IEmailSender>("sink").Should().BeOfType<DevEmailSender>();
    }
}
