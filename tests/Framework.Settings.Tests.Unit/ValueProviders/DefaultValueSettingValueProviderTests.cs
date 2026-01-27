// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;
using Framework.Settings.ValueProviders;
using Framework.Settings.Values;
using Framework.Testing.Tests;

namespace Tests.ValueProviders;

public sealed class DefaultValueSettingValueProviderTests : TestBase
{
    private readonly DefaultValueSettingValueProvider _sut = new();

    [Fact]
    public async Task should_return_default_value()
    {
        // given
        var setting = new SettingDefinition("test.setting", defaultValue: "default-value");

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().Be("default-value");
    }

    [Fact]
    public async Task should_return_null_when_no_default()
    {
        // given
        var setting = new SettingDefinition("test.setting");

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_provider_name()
    {
        // when & then
        _sut.Name.Should().Be(SettingValueProviderNames.DefaultValue);
        DefaultValueSettingValueProvider.ProviderName.Should().Be("DefaultValue");
    }

    [Fact]
    public async Task should_get_all_default_values()
    {
        // given
        var settings = new[]
        {
            new SettingDefinition("setting1", defaultValue: "value1"),
            new SettingDefinition("setting2", defaultValue: "value2"),
            new SettingDefinition("setting3"),
        };

        // when
        var result = await _sut.GetAllAsync(settings, cancellationToken: AbortToken);

        // then
        result.Should().HaveCount(3);
        result.Should().Contain(v => v.Name == "setting1" && v.Value == "value1");
        result.Should().Contain(v => v.Name == "setting2" && v.Value == "value2");
        result.Should().Contain(v => v.Name == "setting3" && v.Value == null);
    }
}
