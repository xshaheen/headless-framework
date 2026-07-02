// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Tests for <see cref="IEmailSenderProvider"/> keyed-service resolution.</summary>
public sealed class EmailSenderProviderTests
{
    [Fact]
    public async Task should_resolve_sender_registered_under_known_name()
    {
        // given
        var named = Substitute.For<IEmailSender>();
        var services = new ServiceCollection();
        services.AddKeyedSingleton("marketing", named);
        services.AddEmailSenderProvider();
        await using var provider = services.BuildServiceProvider();
        var senderProvider = provider.GetRequiredService<IEmailSenderProvider>();

        // when & then
        senderProvider.GetSender("marketing").Should().BeSameAs(named);
        senderProvider.GetSenderOrNull("marketing").Should().BeSameAs(named);
    }

    [Fact]
    public async Task should_return_null_for_unknown_name_from_get_sender_or_null()
    {
        // given
        var services = new ServiceCollection();
        services.AddEmailSenderProvider();
        await using var provider = services.BuildServiceProvider();
        var senderProvider = provider.GetRequiredService<IEmailSenderProvider>();

        // when & then
        senderProvider.GetSenderOrNull("unknown").Should().BeNull();
    }

    [Fact]
    public async Task should_throw_for_unknown_name_from_get_sender()
    {
        // given
        var services = new ServiceCollection();
        services.AddEmailSenderProvider();
        await using var provider = services.BuildServiceProvider();
        var senderProvider = provider.GetRequiredService<IEmailSenderProvider>();

        // when
        var act = () => senderProvider.GetSender("unknown");

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*'unknown'*AddNamed*");
    }

    [Fact]
    public async Task should_guard_against_null_or_empty_names()
    {
        // given
        var services = new ServiceCollection();
        services.AddEmailSenderProvider();
        await using var provider = services.BuildServiceProvider();
        var senderProvider = provider.GetRequiredService<IEmailSenderProvider>();

        // when & then
        var getNull = () => senderProvider.GetSender(null!);
        var getEmpty = () => senderProvider.GetSender("");
        var getOrNullNull = () => senderProvider.GetSenderOrNull(null!);
        var getOrNullEmpty = () => senderProvider.GetSenderOrNull("");

        getNull.Should().Throw<ArgumentException>();
        getEmpty.Should().Throw<ArgumentException>();
        getOrNullNull.Should().Throw<ArgumentException>();
        getOrNullEmpty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void add_email_sender_provider_should_be_idempotent()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddEmailSenderProvider();
        services.AddEmailSenderProvider();

        // then
        services.Count(d => d.ServiceType == typeof(IEmailSenderProvider)).Should().Be(1);
    }
}
