// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Values;

public sealed class SettingManagerExtensionsTests : TestBase
{
    private readonly ISettingManager _settingManager;

    public SettingManagerExtensionsTests()
    {
        _settingManager = Substitute.For<ISettingManager>();
    }

    #region IsTrueAsync

    [Fact]
    public async Task should_return_true_when_value_is_true()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns("true");

        // when
        var result = await _settingManager.IsTrueAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_true_when_value_is_TRUE_case_insensitive()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns("TRUE");

        // when
        var result = await _settingManager.IsTrueAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_when_value_is_not_true()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns("false");

        // when
        var result = await _settingManager.IsTrueAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_pass_provider_name_and_key_to_IsTrue()
    {
        // given
        const string settingName = "TestSetting";
        const string providerName = "TestProvider";
        const string providerKey = "test-key";

        _settingManager.FindAsync(settingName, providerName, providerKey, false, AbortToken).Returns("true");

        // when
        var result = await _settingManager.IsTrueAsync(
            settingName,
            providerName,
            providerKey,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
        await _settingManager.Received(1).FindAsync(settingName, providerName, providerKey, false, AbortToken);
    }

    #endregion

    #region IsFalseAsync

    [Fact]
    public async Task should_return_true_when_value_is_false()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns("false");

        // when
        var result = await _settingManager.IsFalseAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_true_when_value_is_FALSE_case_insensitive()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns("FALSE");

        // when
        var result = await _settingManager.IsFalseAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_when_value_is_not_false()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns("true");

        // when
        var result = await _settingManager.IsFalseAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeFalse();
    }

    #endregion

    #region FindAsync<T>

    [Fact]
    public async Task should_deserialize_typed_value()
    {
        // given
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Name = "Test", Count = 42 };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns(json);

        // when
        var result = await _settingManager.FindAsync<TestSettings>(settingName, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Count.Should().Be(42);
    }

    [Fact]
    public async Task should_return_default_when_value_is_null()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns((string?)null);

        // when
        var result = await _settingManager.FindAsync<TestSettings>(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_default_when_value_is_empty()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns(string.Empty);

        // when
        var result = await _settingManager.FindAsync<TestSettings>(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_deserialize_int_value()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns("42");

        // when
        var result = await _settingManager.FindAsync<int>(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(42);
    }

    [Fact]
    public async Task should_deserialize_bool_value()
    {
        // given
        const string settingName = "TestSetting";
        _settingManager.FindAsync(settingName, null, null, true, AbortToken).Returns("true");

        // when
        var result = await _settingManager.FindAsync<bool>(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    #endregion

    #region SetAsync<T>

    [Fact]
    public async Task should_serialize_and_set_typed_value()
    {
        // given
        const string settingName = "TestSetting";
        const string providerName = "TestProvider";
        var testObj = new TestSettings { Name = "Test", Count = 42 };
        var expectedJson = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        // when
        await _settingManager.SetAsync(
            settingName,
            testObj,
            providerName,
            providerKey: null,
            cancellationToken: AbortToken
        );

        // then
        await _settingManager.Received(1).SetAsync(settingName, expectedJson, providerName, null, false, AbortToken);
    }

    [Fact]
    public async Task should_serialize_null_value()
    {
        // given
        const string settingName = "TestSetting";
        const string providerName = "TestProvider";

        // when
        await _settingManager.SetAsync<TestSettings>(
            settingName,
            null,
            providerName,
            providerKey: null,
            cancellationToken: AbortToken
        );

        // then
        await _settingManager.Received(1).SetAsync(settingName, "null", providerName, null, false, AbortToken);
    }

    [Fact]
    public async Task should_pass_force_to_set_flag()
    {
        // given
        const string settingName = "TestSetting";
        const string providerName = "TestProvider";
        const string providerKey = "key-123";

        // when
        await _settingManager.SetAsync(
            settingName,
            42,
            providerName,
            providerKey,
            forceToSet: true,
            cancellationToken: AbortToken
        );

        // then
        await _settingManager.Received(1).SetAsync(settingName, "42", providerName, providerKey, true, AbortToken);
    }

    #endregion

    private sealed class TestSettings
    {
        public string Name { get; init; } = "";
        public int Count { get; init; }
    }
}
