// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class DevSetupTests
{
    [Fact]
    public void default_use_dev_should_resolve_unkeyed_sender_with_same_instance_bulk_forward()
    {
        // given
        var services = new ServiceCollection();
        var path = Path.Combine(Path.GetTempPath(), $"sms-{Guid.NewGuid():N}.txt");

        // when
        services.AddHeadlessSms(setup => setup.UseDevelopment(path));
        using var provider = services.BuildServiceProvider();

        // then
        var sender = provider.GetRequiredService<ISmsSender>();
        sender.Should().BeOfType<DevSmsSender>();
        provider.GetRequiredService<IBulkSmsSender>().Should().BeSameAs(sender);
    }

    [Fact]
    public void default_use_noop_should_resolve_unkeyed_sender_with_same_instance_bulk_forward()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSms(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        // then
        var sender = provider.GetRequiredService<ISmsSender>();
        sender.Should().BeOfType<NoopSmsSender>();
        provider.GetRequiredService<IBulkSmsSender>().Should().BeSameAs(sender);
    }

    [Fact]
    public void named_dev_instance_should_resolve_via_factory_and_keyed_di_distinct_from_default()
    {
        // given
        var services = new ServiceCollection();
        var path = Path.Combine(Path.GetTempPath(), $"sms-{Guid.NewGuid():N}.txt");

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("audit", instance => instance.UseDevelopment(path));
        });
        using var provider = services.BuildServiceProvider();

        // then
        var defaultSender = provider.GetRequiredService<ISmsSender>();
        var namedSender = provider.GetRequiredService<ISmsSenderProvider>().GetSender("audit");
        namedSender.Should().BeOfType<DevSmsSender>();
        namedSender.Should().NotBeSameAs(defaultSender);
        provider.GetRequiredKeyedService<ISmsSender>("audit").Should().BeSameAs(namedSender);
    }

    [Fact]
    public void named_dev_instance_should_forward_keyed_bulk_sender_to_same_instance()
    {
        // given
        var services = new ServiceCollection();
        var path = Path.Combine(Path.GetTempPath(), $"sms-{Guid.NewGuid():N}.txt");

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("audit", instance => instance.UseDevelopment(path));
        });
        using var provider = services.BuildServiceProvider();

        // then - the capability probe works on the factory-returned sender and the keyed forward matches
        var namedSender = provider.GetRequiredService<ISmsSenderProvider>().GetSender("audit");
        namedSender.Should().BeAssignableTo<IBulkSmsSender>();
        provider.GetRequiredKeyedService<IBulkSmsSender>("audit").Should().BeSameAs(namedSender);
    }

    [Fact]
    public void named_noop_instance_should_forward_keyed_bulk_sender_to_same_instance()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSms(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("sink", static instance => instance.UseNoop());
        });
        using var provider = services.BuildServiceProvider();

        // then
        var namedSender = provider.GetRequiredService<ISmsSenderProvider>().GetSender("sink");
        namedSender.Should().BeOfType<NoopSmsSender>();
        namedSender.Should().NotBeSameAs(provider.GetRequiredService<ISmsSender>());
        provider.GetRequiredKeyedService<IBulkSmsSender>("sink").Should().BeSameAs(namedSender);
    }

    [Fact]
    public void two_named_dev_instances_should_stay_isolated()
    {
        // given
        var services = new ServiceCollection();
        var firstPath = Path.Combine(Path.GetTempPath(), $"sms-{Guid.NewGuid():N}.txt");
        var secondPath = Path.Combine(Path.GetTempPath(), $"sms-{Guid.NewGuid():N}.txt");

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("first", instance => instance.UseDevelopment(firstPath));
            setup.AddNamed("second", instance => instance.UseDevelopment(secondPath));
        });
        using var provider = services.BuildServiceProvider();

        // then
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();
        var first = senderProvider.GetSender("first");
        var second = senderProvider.GetSender("second");
        first.Should().NotBeSameAs(second);
        provider.GetRequiredKeyedService<IBulkSmsSender>("first").Should().BeSameAs(first);
        provider.GetRequiredKeyedService<IBulkSmsSender>("second").Should().BeSameAs(second);
    }
}
