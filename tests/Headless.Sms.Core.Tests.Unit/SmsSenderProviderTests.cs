// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
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
            setup.AddNamed(
                "audit",
                static instance => instance.UseDevelopment(Path.Combine(Path.GetTempPath(), "sms.txt"))
            );
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void should_resolve_known_name_when_get_sender()
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
    public void should_return_null_for_unknown_name_when_get_sender_or_null()
    {
        // given
        using var provider = _BuildProvider();

        // when / then - AE2
        provider.GetRequiredService<ISmsSenderProvider>().GetSenderOrNull("nope").Should().BeNull();
    }

    [Fact]
    public void should_throw_for_unknown_name_with_message_naming_add_named_when_get_sender()
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
    public void should_guard_null_and_whitespace_names_when_get_sender()
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
    public void should_guard_null_and_whitespace_names_when_get_sender_or_null()
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
    public void should_not_be_reachable_by_name_when_default_sender()
    {
        // given - the default is unkeyed by design; the factory only surfaces named (keyed) instances.
        using var provider = _BuildProvider();

        // when / then
        provider.GetRequiredService<ISmsSenderProvider>().GetSenderOrNull("default").Should().BeNull();
    }

    [Fact]
    public void should_list_named_instances_and_exclude_the_default_when_registered_names()
    {
        // given
        using var provider = _BuildProvider();

        // when
        var names = provider.GetRequiredService<ISmsSenderProvider>().RegisteredNames;

        // then - use RegisteredNames to validate externally supplied names before resolving.
        names.Should().BeEquivalentTo(["audit"]);
        names.Contains("audit").Should().BeTrue();
        names.Contains("nope").Should().BeFalse();
    }
}
