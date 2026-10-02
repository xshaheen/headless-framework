// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;
using Headless.Settings.Definitions;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Values;

public sealed class SettingsSnapshotRegistrationTests : TestBase
{
    [Fact]
    public void should_throw_when_no_names_are_given()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddSettingsSnapshot<string>(snapshot => snapshot.Bind(_ => "x"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Names*");
    }

    [Fact]
    public void should_throw_when_no_bind_function_is_given()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddSettingsSnapshot<string>(snapshot => snapshot.Names("App.Theme"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Bind*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_throw_when_the_backstop_is_not_positive(int seconds)
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddSettingsSnapshot<string>(snapshot =>
                snapshot.Names("App.Theme").Bind(_ => "x").Backstop(TimeSpan.FromSeconds(seconds))
            );

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void should_throw_when_a_name_is_blank(string name)
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddSettingsSnapshot<string>(snapshot => snapshot.Names(name).Bind(_ => "x"));

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_the_same_type_is_registered_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddSettingsSnapshot<string>(snapshot => snapshot.Names("App.Theme").Bind(_ => "x"));

        // when
        var act = () => services.AddSettingsSnapshot<string>(snapshot => snapshot.Names("App.Other").Bind(_ => "y"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    [Fact]
    public void should_resolve_one_singleton_and_register_the_hosted_service_once()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISettingManager>());
        services.AddSingleton(Substitute.For<ISettingDefinitionManager>());
        services.AddSettingsSnapshot<string>(snapshot => snapshot.Names("App.Theme").Bind(_ => "x"));
        services.AddSettingsSnapshot<int>(snapshot => snapshot.Names("App.Limit").Bind(_ => 1));
        using var provider = services.BuildServiceProvider();

        // when
        var first = provider.GetRequiredService<ISettingsSnapshot<string>>();
        var second = provider.GetRequiredService<ISettingsSnapshot<string>>();

        // then
        first.Should().BeSameAs(second);
        provider.GetServices<IHostedService>().OfType<SettingsSnapshotHostedService>().Should().ContainSingle();
    }
}
