// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class AddHeadlessSmsTests
{
    [Fact]
    public void should_register_the_single_selected_provider()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSms(setup => setup.UseNoop());

        // then
        using var provider = services.BuildServiceProvider();
        provider.GetService<ISmsSender>().Should().NotBeNull();
    }

    [Fact]
    public void should_register_bulk_sender_for_noop_provider()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSms(setup => setup.UseNoop());

        // then
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBulkSmsSender>().Should().BeSameAs(provider.GetRequiredService<ISmsSender>());
    }

    [Fact]
    public void should_register_bulk_sender_for_dev_provider()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSms(setup => setup.UseDevelopment(Path.Combine(Path.GetTempPath(), "sms.txt")));

        // then
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBulkSmsSender>().Should().BeSameAs(provider.GetRequiredService<ISmsSender>());
    }

    [Fact]
    public void should_not_register_default_sender_when_no_provider_is_selected()
    {
        // given - the default slot is optional; an empty setup registers only the factory.
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSms(static _ => { });

        // then
        using var provider = services.BuildServiceProvider();
        provider.GetService<ISmsSender>().Should().BeNull();
        provider.GetService<ISmsSenderProvider>().Should().NotBeNull();
    }

    [Fact]
    public void should_throw_when_multiple_providers_are_selected()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHeadlessSms(static setup => setup.UseNoop().UseNoop());

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*at most one default*");
    }

    [Fact]
    public void should_throw_when_called_more_than_once()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup => setup.UseNoop());

        // when
        var act = () => services.AddHeadlessSms(setup => setup.UseNoop());

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }
}
