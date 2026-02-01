// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Definitions;
using Headless.Settings.Models;
using Headless.Testing.Tests;
using Tests.Fakes;

namespace Tests.Definitions;

public sealed class SettingDefinitionManagerTests : TestBase
{
    private readonly FakeStaticSettingDefinitionStore _staticStore = new();
    private readonly IDynamicSettingDefinitionStore _dynamicStore = Substitute.For<IDynamicSettingDefinitionStore>();
    private readonly SettingDefinitionManager _sut;

    public SettingDefinitionManagerTests()
    {
        _sut = new SettingDefinitionManager(_staticStore, _dynamicStore);
    }

    [Fact]
    public async Task should_find_in_static_store_first()
    {
        // given
        var staticDefinition = new SettingDefinition("TestSetting", "static-default");
        var dynamicDefinition = new SettingDefinition("TestSetting", "dynamic-default");
        _staticStore.Add(staticDefinition);
        _dynamicStore.GetOrDefaultAsync("TestSetting", Arg.Any<CancellationToken>()).Returns(dynamicDefinition);

        // when
        var result = await _sut.FindAsync("TestSetting", AbortToken);

        // then
        result.Should().BeSameAs(staticDefinition);
        await _dynamicStore.DidNotReceive().GetOrDefaultAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fallback_to_dynamic_store()
    {
        // given
        var dynamicDefinition = new SettingDefinition("DynamicSetting", "dynamic-default");
        _dynamicStore.GetOrDefaultAsync("DynamicSetting", Arg.Any<CancellationToken>()).Returns(dynamicDefinition);

        // when
        var result = await _sut.FindAsync("DynamicSetting", AbortToken);

        // then
        result.Should().BeSameAs(dynamicDefinition);
        await _dynamicStore.Received(1).GetOrDefaultAsync("DynamicSetting", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_null_when_not_found()
    {
        // given
        _dynamicStore
            .GetOrDefaultAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SettingDefinition?)null);

        // when
        var result = await _sut.FindAsync("NonExistent", AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_name_is_null()
    {
        // when
        var act = () => _sut.FindAsync(null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_get_all_from_both_stores()
    {
        // given
        var staticDef1 = new SettingDefinition("Static1");
        var staticDef2 = new SettingDefinition("Static2");
        var dynamicDef1 = new SettingDefinition("Dynamic1");
        var dynamicDef2 = new SettingDefinition("Dynamic2");

        _staticStore.AddRange(staticDef1, staticDef2);
        _dynamicStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns([dynamicDef1, dynamicDef2]);

        // when
        var result = await _sut.GetAllAsync(AbortToken);

        // then
        result.Should().HaveCount(4);
        result.Should().Contain(staticDef1);
        result.Should().Contain(staticDef2);
        result.Should().Contain(dynamicDef1);
        result.Should().Contain(dynamicDef2);
    }

    [Fact]
    public async Task should_prefer_static_over_dynamic_duplicates()
    {
        // given
        var staticDefinition = new SettingDefinition("DuplicateSetting", "static-value");
        var dynamicDefinition = new SettingDefinition("DuplicateSetting", "dynamic-value");

        _staticStore.Add(staticDefinition);
        _dynamicStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns([dynamicDefinition]);

        // when
        var result = await _sut.GetAllAsync(AbortToken);

        // then
        result.Should().ContainSingle();
        result.Single().Should().BeSameAs(staticDefinition);
    }

    [Fact]
    public async Task should_return_unique_definitions()
    {
        // given
        var staticDef = new SettingDefinition("SharedName", "static");
        var dynamicDef1 = new SettingDefinition("SharedName", "dynamic");
        var dynamicDef2 = new SettingDefinition("UniqueDynamic", "value");

        _staticStore.Add(staticDef);
        _dynamicStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns([dynamicDef1, dynamicDef2]);

        // when
        var result = await _sut.GetAllAsync(AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Select(d => d.Name).Should().OnlyHaveUniqueItems();
        result.Should().Contain(d => d.Name == "SharedName" && d.DefaultValue == "static");
        result.Should().Contain(d => d.Name == "UniqueDynamic");
    }
}
