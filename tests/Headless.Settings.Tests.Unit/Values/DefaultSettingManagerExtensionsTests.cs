// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Values;

public sealed class DefaultSettingManagerExtensionsTests : TestBase
{
    private readonly ISettingManager _settingManager;

    public DefaultSettingManagerExtensionsTests()
    {
        _settingManager = Substitute.For<ISettingManager>();
    }

    #region IsTrueDefaultAsync

    [Fact]
    public async Task should_call_with_default_provider_for_is_true()
    {
        // given
        var settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, true, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueDefaultAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_is_true_default()
    {
        // given
        var settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, false, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueDefaultAsync(
            settingName,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, false, AbortToken);
    }

    #endregion

    #region IsFalseDefaultAsync

    [Fact]
    public async Task should_call_with_default_provider_for_is_false()
    {
        // given
        var settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, true, AbortToken)
            .Returns("false");

        // when
        var result = await _settingManager.IsFalseDefaultAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, true, AbortToken);
    }

    #endregion

    #region FindDefaultAsync<T>

    [Fact]
    public async Task should_find_typed_from_default_provider()
    {
        // given
        var settingName = "TestSetting";
        var testObj = new TestSettings { Value = "test" };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, true, AbortToken)
            .Returns(json);

        // when
        var result = await _settingManager.FindDefaultAsync<TestSettings>(settingName, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be("test");
    }

    #endregion

    #region FindDefaultAsync (string)

    [Fact]
    public async Task should_find_string_from_default_provider()
    {
        // given
        var settingName = "TestSetting";
        var expectedValue = "test-value";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, true, AbortToken)
            .Returns(expectedValue);

        // when
        var result = await _settingManager.FindDefaultAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
    }

    [Fact]
    public async Task should_pass_fallback_for_find_default()
    {
        // given
        var settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, false, AbortToken)
            .Returns("value");

        // when
        await _settingManager.FindDefaultAsync(settingName, fallback: false, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.DefaultValue, null, false, AbortToken);
    }

    #endregion

    #region GetAllDefaultAsync

    [Fact]
    public async Task should_get_all_from_default_provider()
    {
        // given
        List<SettingValue> expectedValues = [new("Setting1", "value1"), new("Setting2", "value2")];

        _settingManager
            .GetAllAsync(SettingValueProviderNames.DefaultValue, null, true, AbortToken)
            .Returns(expectedValues);

        // when
        var result = await _settingManager.GetAllDefaultAsync(cancellationToken: AbortToken);

        // then
        result.Should().BeEquivalentTo(expectedValues);
        await _settingManager.Received(1).GetAllAsync(SettingValueProviderNames.DefaultValue, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_get_all_default()
    {
        // given
        _settingManager.GetAllAsync(SettingValueProviderNames.DefaultValue, null, false, AbortToken).Returns([]);

        // when
        await _settingManager.GetAllDefaultAsync(fallback: false, cancellationToken: AbortToken);

        // then
        await _settingManager.Received(1).GetAllAsync(SettingValueProviderNames.DefaultValue, null, false, AbortToken);
    }

    #endregion

    private sealed class TestSettings
    {
        public string Value { get; init; } = "";
    }
}
