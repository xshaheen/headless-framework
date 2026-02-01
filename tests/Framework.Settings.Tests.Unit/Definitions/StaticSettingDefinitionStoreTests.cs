// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Framework.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Definitions;

public sealed class StaticSettingDefinitionStoreTests : TestBase
{
    [Fact]
    public async Task should_get_all_static_definitions()
    {
        // given
        var sut = _CreateStore(new TestSettingDefinitionProvider());

        // when
        var result = await sut.GetAllAsync(AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(d => d.Name == "Setting1");
        result.Should().Contain(d => d.Name == "Setting2");
    }

    [Fact]
    public async Task should_get_definition_by_name()
    {
        // given
        var sut = _CreateStore(new TestSettingDefinitionProvider());

        // when
        var result = await sut.GetOrDefaultAsync("Setting1", AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be("Setting1");
        result.DefaultValue.Should().Be("default1");
    }

    [Fact]
    public async Task should_return_null_when_not_found()
    {
        // given
        var sut = _CreateStore(new TestSettingDefinitionProvider());

        // when
        var result = await sut.GetOrDefaultAsync("NonExistent", AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_collect_from_all_providers()
    {
        // given
        var sut = _CreateStore(new TestSettingDefinitionProvider(), new AnotherSettingDefinitionProvider());

        // when
        var result = await sut.GetAllAsync(AbortToken);

        // then
        result.Should().HaveCount(3);
        result.Should().Contain(d => d.Name == "Setting1");
        result.Should().Contain(d => d.Name == "Setting2");
        result.Should().Contain(d => d.Name == "AnotherSetting");
    }

    private static StaticSettingDefinitionStore _CreateStore(params ISettingDefinitionProvider[] providers)
    {
        var services = new ServiceCollection();
        var options = new SettingManagementProvidersOptions();

        foreach (var provider in providers)
        {
            var providerType = provider.GetType();
            services.AddSingleton(providerType, provider);
            options.DefinitionProviders.Add(providerType);
        }

        var serviceProvider = services.BuildServiceProvider();
        var optionsWrapper = Options.Create(options);

        return new StaticSettingDefinitionStore(serviceProvider, optionsWrapper);
    }

    private sealed class TestSettingDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add(new SettingDefinition("Setting1", "default1"), new SettingDefinition("Setting2", "default2"));
        }
    }

    private sealed class AnotherSettingDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add(new SettingDefinition("AnotherSetting", "another-default"));
        }
    }
}
