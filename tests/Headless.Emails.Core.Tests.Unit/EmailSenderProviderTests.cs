// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

/// <summary>Tests for <see cref="IEmailSenderProvider"/> keyed-service resolution.</summary>
public sealed class EmailSenderProviderTests
{
    // Registers the factory the way the setup gate does — TryAdd of the keyed-service provider carrying the
    // registered instance names.
    private static void _AddEmailSenderProvider(IServiceCollection services, params string[] names)
    {
        var registeredNames = names.ToFrozenSet(StringComparer.Ordinal);
        services.TryAddSingleton<IEmailSenderProvider>(provider => new KeyedServiceEmailSenderProvider(
            provider,
            registeredNames
        ));
    }

    [Fact]
    public async Task should_resolve_sender_registered_under_known_name()
    {
        // given
        var named = Substitute.For<IEmailSender>();
        var services = new ServiceCollection();
        services.AddKeyedSingleton("marketing", named);
        _AddEmailSenderProvider(services, "marketing");
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
        _AddEmailSenderProvider(services);
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
        _AddEmailSenderProvider(services);
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
        _AddEmailSenderProvider(services);
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
    public void email_sender_provider_registration_should_be_idempotent()
    {
        // given
        var services = new ServiceCollection();

        // when
        _AddEmailSenderProvider(services);
        _AddEmailSenderProvider(services);

        // then
        services.Count(d => d.ServiceType == typeof(IEmailSenderProvider)).Should().Be(1);
    }

    [Fact]
    public void registered_names_should_list_named_instances_and_exclude_the_default()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessEmails(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("marketing", static instance => instance.UseDevelopment("out.txt"));
            setup.AddNamed("alerts", static instance => instance.UseNoop());
        });
        using var provider = services.BuildServiceProvider();

        // when
        var names = provider.GetRequiredService<IEmailSenderProvider>().RegisteredNames;

        // then - use RegisteredNames to validate externally supplied names before resolving.
        names.Should().BeEquivalentTo(["marketing", "alerts"]);
        names.Contains("marketing").Should().BeTrue();
        names.Contains("nope").Should().BeFalse();
    }
}
