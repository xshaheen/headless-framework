// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Tests;

namespace Tests.Values;

public sealed class ConfigurationValueSettingManagerExtensionsTests : TestBase
{
    private readonly ISettingManager _settingManager = Substitute.For<ISettingManager>();

    #region IsTrueInConfigurationAsync

    [Fact]
    public async Task should_call_with_configuration_provider_for_is_true()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns(new SettingValue(settingName, "true"));

        // when
        var result = await _settingManager.IsTrueInConfigurationAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_is_true_configuration()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken)
            .Returns(new SettingValue(settingName, "true"));

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
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken);
    }

    #endregion

    #region IsFalseInConfigurationAsync

    [Fact]
    public async Task should_call_with_configuration_provider_for_is_false()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns(new SettingValue(settingName, "false"));

        // when
        var result = await _settingManager.IsFalseInConfigurationAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_is_false_configuration()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken)
            .Returns(new SettingValue(settingName, "false"));

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

    #region GetInConfigurationAsync<T>

    [Fact]
    public async Task should_find_typed_from_configuration_provider()
    {
        // given
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "config-test" };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns(new SettingValue(settingName, json));

        // when
        var result = await _settingManager.GetInConfigurationAsync<TestSettings>(
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
        const string settingName = "TestSetting";
        const string json = "{}";

        _settingManager
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken)
            .Returns(new SettingValue(settingName, json));

        // when
        await _settingManager.GetInConfigurationAsync<TestSettings>(
            settingName,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        await _settingManager
            .Received(1)
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken);
    }

    #endregion

    #region GetInConfigurationAsync (string)

    [Fact]
    public async Task should_find_string_from_configuration_provider()
    {
        // given
        const string settingName = "TestSetting";
        const string expectedValue = "config-value";

        _settingManager
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken)
            .Returns(new SettingValue(settingName, expectedValue));

        // when
        var result = await _settingManager.GetInConfigurationAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
        await _settingManager
            .Received(1)
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_find_string_configuration()
    {
        // given
        const string settingName = "TestSetting";

        _settingManager
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken)
            .Returns(new SettingValue(settingName, "value"));

        // when
        await _settingManager.GetInConfigurationAsync(settingName, fallback: false, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .GetAsync(settingName, SettingValueProviderNames.Configuration, null, false, AbortToken);
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
