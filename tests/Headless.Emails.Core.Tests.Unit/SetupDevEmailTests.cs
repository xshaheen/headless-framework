// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupDevEmailTests
{
    [Fact]
    public void should_resolve_default_development_sender()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessEmails(setup => setup.UseDevelopment("out.txt"));
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<DevEmailSender>();
    }

    [Fact]
    public void should_resolve_default_noop_sender()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessEmails(setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NoopEmailSender>();
    }

    [Fact]
    public void development_sender_should_be_singleton()
    {
        // given — DevEmailSender serializes file appends through an instance-level lock, so the
        // registration must produce a single shared instance.
        var services = new ServiceCollection();
        services.AddHeadlessEmails(setup => setup.UseDevelopment("out.txt"));
        using var provider = services.BuildServiceProvider();

        // when
        var first = provider.GetRequiredService<IEmailSender>();
        var second = provider.GetRequiredService<IEmailSender>();

        // then
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void use_development_should_reject_empty_path()
    {
        // given
        var services = new ServiceCollection();

        // when — the path is validated at setup time, not at first send
        var act = () => services.AddHeadlessEmails(setup => setup.UseDevelopment(""));

        // then
        act.Should().Throw<ArgumentException>();
    }
}
