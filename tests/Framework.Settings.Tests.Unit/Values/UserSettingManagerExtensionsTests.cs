// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Framework.Settings.Models;
using Framework.Settings.Values;
using Framework.Testing.Tests;
using NSubstitute;

namespace Tests.Values;

public sealed class UserSettingManagerExtensionsTests : TestBase
{
    private readonly ISettingManager _settingManager;

    public UserSettingManagerExtensionsTests()
    {
        _settingManager = Substitute.For<ISettingManager>();
    }

    #region IsTrueForUserAsync

    [Fact]
    public async Task should_call_with_user_provider_and_user_id()
    {
        // given
        var userId = "user-123";
        var settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.User, userId, true, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueForUserAsync(userId, settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.User, userId, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_is_true_user()
    {
        // given
        var userId = "user-123";
        var settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.User, userId, false, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueForUserAsync(
            userId,
            settingName,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
    }

    #endregion

    #region IsTrueForCurrentUserAsync

    [Fact]
    public async Task should_call_with_user_provider_and_null_key_for_current()
    {
        // given
        var settingName = "TestSetting";

        _settingManager.FindAsync(settingName, SettingValueProviderNames.User, null, true, AbortToken).Returns("true");

        // when
        var result = await _settingManager.IsTrueForCurrentUserAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.User, null, true, AbortToken);
    }

    #endregion

    #region IsFalseForUserAsync

    [Fact]
    public async Task should_call_with_user_provider_for_is_false()
    {
        // given
        var userId = "user-456";
        var settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.User, userId, true, AbortToken)
            .Returns("false");

        // when
        var result = await _settingManager.IsFalseForUserAsync(userId, settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    #endregion

    #region IsFalseForCurrentUserAsync

    [Fact]
    public async Task should_call_with_user_provider_and_null_key_for_is_false_current()
    {
        // given
        var settingName = "TestSetting";

        _settingManager.FindAsync(settingName, SettingValueProviderNames.User, null, true, AbortToken).Returns("false");

        // when
        var result = await _settingManager.IsFalseForCurrentUserAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    #endregion

    #region FindForUserAsync<T>

    [Fact]
    public async Task should_find_typed_from_user_provider()
    {
        // given
        var userId = "user-123";
        var settingName = "TestSetting";
        var testObj = new TestSettings { Value = "user-test" };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager.FindAsync(settingName, SettingValueProviderNames.User, userId, true, AbortToken).Returns(json);

        // when
        var result = await _settingManager.FindForUserAsync<TestSettings>(
            userId,
            settingName,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be("user-test");
    }

    #endregion

    #region FindForCurrentUserAsync<T>

    [Fact]
    public async Task should_find_typed_from_current_user()
    {
        // given
        var settingName = "TestSetting";
        var testObj = new TestSettings { Value = "current-user-test" };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager.FindAsync(settingName, SettingValueProviderNames.User, null, true, AbortToken).Returns(json);

        // when
        var result = await _settingManager.FindForCurrentUserAsync<TestSettings>(
            settingName,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be("current-user-test");
    }

    #endregion

    #region FindForUserAsync (string)

    [Fact]
    public async Task should_find_string_from_user_provider()
    {
        // given
        var userId = "user-789";
        var settingName = "TestSetting";
        var expectedValue = "user-value";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.User, userId, true, AbortToken)
            .Returns(expectedValue);

        // when
        var result = await _settingManager.FindForUserAsync(userId, settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
    }

    #endregion

    #region FindForCurrentUserAsync (string)

    [Fact]
    public async Task should_find_string_from_current_user()
    {
        // given
        var settingName = "TestSetting";
        var expectedValue = "current-user-value";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.User, null, true, AbortToken)
            .Returns(expectedValue);

        // when
        var result = await _settingManager.FindForCurrentUserAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
    }

    #endregion

    #region GetAllForUserAsync

    [Fact]
    public async Task should_get_all_from_user_provider()
    {
        // given
        var userId = "user-123";
        var expectedValues = new List<SettingValue> { new("Setting1", "value1"), new("Setting2", "value2") };

        _settingManager.GetAllAsync(SettingValueProviderNames.User, userId, true, AbortToken).Returns(expectedValues);

        // when
        var result = await _settingManager.GetAllForUserAsync(userId, cancellationToken: AbortToken);

        // then
        result.Should().BeEquivalentTo(expectedValues);
    }

    #endregion

    #region GetAllForCurrentUserAsync

    [Fact]
    public async Task should_get_all_from_current_user()
    {
        // given
        var expectedValues = new List<SettingValue> { new("Setting1", "value1") };

        _settingManager.GetAllAsync(SettingValueProviderNames.User, null, true, AbortToken).Returns(expectedValues);

        // when
        var result = await _settingManager.GetAllForCurrentUserAsync(cancellationToken: AbortToken);

        // then
        result.Should().BeEquivalentTo(expectedValues);
    }

    #endregion

    #region SetForUserAsync (string)

    [Fact]
    public async Task should_set_value_for_user_provider()
    {
        // given
        var userId = "user-123";
        var settingName = "TestSetting";
        var value = "new-user-value";

        // when
        await _settingManager.SetForUserAsync(userId, settingName, value, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.User, userId, false, AbortToken);
    }

    [Fact]
    public async Task should_pass_force_to_set_for_user()
    {
        // given
        var userId = "user-123";
        var settingName = "TestSetting";
        var value = "forced-value";

        // when
        await _settingManager.SetForUserAsync(
            userId,
            settingName,
            value,
            forceToSet: true,
            cancellationToken: AbortToken
        );

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.User, userId, true, AbortToken);
    }

    #endregion

    #region SetForCurrentUserAsync (string)

    [Fact]
    public async Task should_set_value_for_current_user()
    {
        // given
        var settingName = "TestSetting";
        var value = "current-user-value";

        // when
        await _settingManager.SetForCurrentUserAsync(settingName, value, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.User, null, false, AbortToken);
    }

    #endregion

    #region SetForUserAsync<T>

    [Fact]
    public async Task should_set_typed_value_for_user()
    {
        // given
        var userId = "user-123";
        var settingName = "TestSetting";
        var testObj = new TestSettings { Value = "typed-user-value" };
        var expectedJson = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        // when
        await _settingManager.SetForUserAsync(userId, settingName, testObj, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, expectedJson, SettingValueProviderNames.User, userId, false, AbortToken);
    }

    #endregion

    #region SetForCurrentUserAsync<T>

    [Fact]
    public async Task should_set_typed_value_for_current_user()
    {
        // given
        var settingName = "TestSetting";
        var testObj = new TestSettings { Value = "typed-current-user-value" };
        var expectedJson = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        // when
        await _settingManager.SetForCurrentUserAsync(settingName, testObj, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, expectedJson, SettingValueProviderNames.User, null, false, AbortToken);
    }

    #endregion

    private sealed class TestSettings
    {
        public string Value { get; init; } = "";
    }
}
