// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SmsSenderProviderTests
{
    private static ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("audit", static instance => instance.UseDev(Path.Combine(Path.GetTempPath(), "sms.txt")));
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void get_sender_should_resolve_known_name()
    {
        // given
        using var provider = _BuildProvider();
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();

        // when
        var sender = senderProvider.GetSender("audit");

        // then - the factory surfaces the same keyed singleton on every call.
        sender.Should().BeSameAs(provider.GetRequiredKeyedService<ISmsSender>("audit"));
        senderProvider.GetSender("audit").Should().BeSameAs(sender);
    }

    [Fact]
    public void get_sender_or_null_should_return_null_for_unknown_name()
    {
        // given
        using var provider = _BuildProvider();

        // when / then - AE2
        provider.GetRequiredService<ISmsSenderProvider>().GetSenderOrNull("nope").Should().BeNull();
    }

    [Fact]
    public void get_sender_should_throw_for_unknown_name_with_message_naming_add_named()
    {
        // given
        using var provider = _BuildProvider();
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();

        // when
        var action = () => senderProvider.GetSender("nope");

        // then - AE2: the message points at AddNamed and the provider Use* members.
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*No SMS sender is registered under the name 'nope'*")
            .WithMessage("*AddNamed*")
            .WithMessage("*UseTwilio*");
    }

    [Fact]
    public void get_sender_should_guard_null_and_whitespace_names()
    {
        // given
        using var provider = _BuildProvider();
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();

        // when / then
        ((Action)(() => senderProvider.GetSender(null!)))
            .Should()
            .Throw<ArgumentException>();
        ((Action)(() => senderProvider.GetSender(" "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void get_sender_or_null_should_guard_null_and_whitespace_names()
    {
        // given
        using var provider = _BuildProvider();
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();

        // when / then
        ((Action)(() => senderProvider.GetSenderOrNull(null!)))
            .Should()
            .Throw<ArgumentException>();
        ((Action)(() => senderProvider.GetSenderOrNull(" "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void default_sender_should_not_be_reachable_by_name()
    {
        // given - the default is unkeyed by design; the factory only surfaces named (keyed) instances.
        using var provider = _BuildProvider();

        // when / then
        provider.GetRequiredService<ISmsSenderProvider>().GetSenderOrNull("default").Should().BeNull();
    }
}
