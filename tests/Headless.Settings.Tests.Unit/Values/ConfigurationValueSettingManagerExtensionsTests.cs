// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Values;

public sealed class ConfigurationValueSettingManagerExtensionsTests : TestBase
{
    private readonly ISettingManager _settingManager;

    public ConfigurationValueSettingManagerExtensionsTests()
    {
        _settingManager = Substitute.For<ISettingManager>();
    }

    #region IsTrueInConfigurationAsync

    [Fact]
    public async Task should_call_with_configuration_provider_for_is_true()
    {
        // given
        var settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueInConfigurationAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_is_true_configuration()
    {
        // given
        var settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueInConfigurationAsync(
            settingName,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken);
    }

    #endregion

    #region IsFalseInConfigurationAsync

    [Fact]
    public async Task should_call_with_configuration_provider_for_is_false()
    {
        // given
        var settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns("false");

        // when
        var result = await _settingManager.IsFalseInConfigurationAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_is_false_configuration()
    {
        // given
        var settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken)
            .Returns("false");

        // when
        var result = await _settingManager.IsFalseInConfigurationAsync(
            settingName,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
    }

    #endregion

    #region FindInConfigurationAsync<T>

    [Fact]
    public async Task should_find_typed_from_configuration_provider()
    {
        // given
        var settingName = "TestSetting";
        var testObj = new TestSettings { Value = "config-test" };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns(json);

        // when
        var result = await _settingManager.FindInConfigurationAsync<TestSettings>(
            settingName,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be("config-test");
    }

    [Fact]
    public async Task should_pass_fallback_for_find_typed_configuration()
    {
        // given
        var settingName = "TestSetting";
        var json = "{}";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken)
            .Returns(json);

        // when
        await _settingManager.FindInConfigurationAsync<TestSettings>(
            settingName,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken);
    }

    #endregion

    #region FindInConfigurationAsync (string)

    [Fact]
    public async Task should_find_string_from_configuration_provider()
    {
        // given
        var settingName = "TestSetting";
        var expectedValue = "config-value";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns(expectedValue);

        // when
        var result = await _settingManager.FindInConfigurationAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_find_string_configuration()
    {
        // given
        var settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken)
            .Returns("value");

        // when
        await _settingManager.FindInConfigurationAsync(settingName, fallback: false, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken);
    }

    #endregion

    #region GetAllInConfigurationAsync

    [Fact]
    public async Task should_get_all_from_configuration_provider()
    {
        // given
        List<SettingValue> expectedValues = [new("Setting1", "value1"), new("Setting2", "value2")];

        _settingManager
            .GetAllAsync(SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns(expectedValues);

        // when
        var result = await _settingManager.GetAllInConfigurationAsync(cancellationToken: AbortToken);

        // then
        result.Should().BeEquivalentTo(expectedValues);
        await _settingManager.Received(1).GetAllAsync(SettingValueProviderNames.Configuration, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_get_all_configuration()
    {
        // given
        _settingManager.GetAllAsync(SettingValueProviderNames.Configuration, null, false, AbortToken).Returns([]);

        // when
        await _settingManager.GetAllInConfigurationAsync(fallback: false, cancellationToken: AbortToken);

        // then
        await _settingManager.Received(1).GetAllAsync(SettingValueProviderNames.Configuration, null, false, AbortToken);
    }

    #endregion

    private sealed class TestSettings
    {
        public string Value { get; init; } = "";
    }
}
