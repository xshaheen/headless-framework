// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;
using Framework.Settings.ValueProviders;
using Framework.Settings.Values;
using Framework.Testing.Tests;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Tests.ValueProviders;

public sealed class ConfigurationSettingValueProviderTests : TestBase
{
    private readonly IConfiguration _configuration = Substitute.For<IConfiguration>();
    private readonly ConfigurationSettingValueProvider _sut;

    public ConfigurationSettingValueProviderTests()
    {
        _sut = new ConfigurationSettingValueProvider(_configuration);
    }

    [Fact]
    public async Task should_read_from_configuration()
    {
        // given
        var setting = new SettingDefinition("app.theme");
        _configuration["Settings:app.theme"].Returns("dark");

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().Be("dark");
    }

    [Fact]
    public async Task should_use_setting_name_as_key()
    {
        // given
        var setting = new SettingDefinition("my.custom.setting");
        _configuration["Settings:my.custom.setting"].Returns("custom-value");

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().Be("custom-value");
        _ = _configuration.Received(1)["Settings:my.custom.setting"];
    }

    [Fact]
    public async Task should_return_null_when_not_in_config()
    {
        // given
        var setting = new SettingDefinition("nonexistent.setting");
        _configuration["Settings:nonexistent.setting"].Returns((string?)null);

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_provider_name()
    {
        // when & then
        _sut.Name.Should().Be(SettingValueProviderNames.Configuration);
        ConfigurationSettingValueProvider.ProviderName.Should().Be("Configuration");
        ConfigurationSettingValueProvider.ConfigurationNamePrefix.Should().Be("Settings:");
    }

    [Fact]
    public async Task should_get_all_from_configuration()
    {
        // given
        var settings = new[]
        {
            new SettingDefinition("setting1"),
            new SettingDefinition("setting2"),
            new SettingDefinition("setting3"),
        };

        _configuration["Settings:setting1"].Returns("value1");
        _configuration["Settings:setting2"].Returns("value2");
        _configuration["Settings:setting3"].Returns((string?)null);

        // when
        var result = await _sut.GetAllAsync(settings, cancellationToken: AbortToken);

        // then
        result.Should().HaveCount(3);
        result.Should().Contain(v => v.Name == "setting1" && v.Value == "value1");
        result.Should().Contain(v => v.Name == "setting2" && v.Value == "value2");
        result.Should().Contain(v => v.Name == "setting3" && v.Value == null);
    }
}
