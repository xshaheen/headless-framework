// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Values;

public sealed class GlobalSettingManagerExtensionsTests : TestBase
{
    private readonly ISettingManager _settingManager;

    public GlobalSettingManagerExtensionsTests()
    {
        _settingManager = Substitute.For<ISettingManager>();
    }

    #region IsTrueGlobalAsync

    [Fact]
    public async Task should_call_with_global_provider_for_is_true()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Global, null, true, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueGlobalAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Global, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_is_true_global()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Global, null, false, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueGlobalAsync(
            settingName,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Global, null, false, AbortToken);
    }

    #endregion

    #region IsFalseGlobalAsync

    [Fact]
    public async Task should_call_with_global_provider_for_is_false()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Global, null, true, AbortToken)
            .Returns("false");

        // when
        var result = await _settingManager.IsFalseGlobalAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Global, null, true, AbortToken);
    }

    #endregion

    #region FindGlobalAsync<T>

    [Fact]
    public async Task should_find_typed_from_global_provider()
    {
        // given
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "global-test" };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager.FindAsync(settingName, SettingValueProviderNames.Global, null, true, AbortToken).Returns(json);

        // when
        var result = await _settingManager.FindGlobalAsync<TestSettings>(settingName, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be("global-test");
    }

    #endregion

    #region FindGlobalAsync (string)

    [Fact]
    public async Task should_find_string_from_global_provider()
    {
        // given
        const string settingName = "TestSetting";
        const string expectedValue = "global-value";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Global, null, true, AbortToken)
            .Returns(expectedValue);

        // when
        var result = await _settingManager.FindGlobalAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Global, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_find_global()
    {
        // given
        const string settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Global, null, false, AbortToken)
            .Returns("value");

        // when
        await _settingManager.FindGlobalAsync(settingName, fallback: false, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Global, null, false, AbortToken);
    }

    #endregion

    #region GetAllGlobalAsync

    [Fact]
    public async Task should_get_all_from_global_provider()
    {
        // given
        List<SettingValue> expectedValues = [new("Setting1", "value1"), new("Setting2", "value2")];

        _settingManager.GetAllAsync(SettingValueProviderNames.Global, null, true, AbortToken).Returns(expectedValues);

        // when
        var result = await _settingManager.GetAllGlobalAsync(cancellationToken: AbortToken);

        // then
        result.Should().BeEquivalentTo(expectedValues);
        await _settingManager.Received(1).GetAllAsync(SettingValueProviderNames.Global, null, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_get_all_global()
    {
        // given
        _settingManager.GetAllAsync(SettingValueProviderNames.Global, null, false, AbortToken).Returns([]);

        // when
        await _settingManager.GetAllGlobalAsync(fallback: false, cancellationToken: AbortToken);

        // then
        await _settingManager.Received(1).GetAllAsync(SettingValueProviderNames.Global, null, false, AbortToken);
    }

    #endregion

    #region SetGlobalAsync (string)

    [Fact]
    public async Task should_set_string_value_for_global_provider()
    {
        // given
        const string settingName = "TestSetting";
        const string value = "new-global-value";

        // when
        await _settingManager.SetGlobalAsync(settingName, value, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.Global, null, false, AbortToken);
    }

    [Fact]
    public async Task should_set_null_string_value_for_global_provider()
    {
        // given
        const string settingName = "TestSetting";

        // when
        await _settingManager.SetGlobalAsync(settingName, (string?)null, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, null, SettingValueProviderNames.Global, null, false, AbortToken);
    }

    #endregion

    #region SetGlobalAsync<T>

    [Fact]
    public async Task should_set_typed_value_for_global_provider()
    {
        // given
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "typed-value" };
        var expectedJson = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        // when
        await _settingManager.SetGlobalAsync(settingName, testObj, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, expectedJson, SettingValueProviderNames.Global, null, false, AbortToken);
    }

    [Fact]
    public async Task should_set_null_typed_value_for_global_provider()
    {
        // given
        const string settingName = "TestSetting";

        // when
        await _settingManager.SetGlobalAsync<TestSettings>(settingName, null, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, "null", SettingValueProviderNames.Global, null, false, AbortToken);
    }

    #endregion

    private sealed class TestSettings
    {
        public string Value { get; init; } = "";
    }
}
