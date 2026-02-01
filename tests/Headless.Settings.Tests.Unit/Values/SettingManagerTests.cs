// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Exceptions;
using Headless.Primitives;
using Headless.Settings.Definitions;
using Headless.Settings.Helpers;
using Headless.Settings.Models;
using Headless.Settings.Resources;
using Headless.Settings.ValueProviders;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;
using Tests.Fakes;

namespace Tests.Values;

public sealed class SettingManagerTests : TestBase
{
    private readonly ISettingDefinitionManager _definitionManager;
    private readonly ISettingValueStore _valueStore;
    private readonly ISettingValueProviderManager _valueProviderManager;
    private readonly ISettingEncryptionService _encryptionService;
    private readonly ISettingsErrorsDescriptor _errorsDescriptor;
    private readonly SettingManager _sut;

    public SettingManagerTests()
    {
        _definitionManager = Substitute.For<ISettingDefinitionManager>();
        _valueStore = Substitute.For<ISettingValueStore>();
        _valueProviderManager = Substitute.For<ISettingValueProviderManager>();
        _encryptionService = Substitute.For<ISettingEncryptionService>();
        _errorsDescriptor = new DefaultSettingsErrorsDescriptor();

        _sut = new SettingManager(
            _definitionManager,
            _valueStore,
            _valueProviderManager,
            _encryptionService,
            _errorsDescriptor
        );
    }

    #region FindAsync

    [Fact]
    public async Task should_find_value_from_providers()
    {
        // given
        const string settingName = "TestSetting";
        const string expectedValue = "test-value";
        var definition = new SettingDefinition(settingName);
        var provider = new FakeSettingValueProvider { Name = "Provider1" };
        provider.SetValue(settingName, expectedValue);

        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider]);

        // when
        var result = await _sut.FindAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
    }

    [Fact]
    public async Task should_throw_when_setting_not_defined()
    {
        // given
        const string settingName = "UndefinedSetting";
        _definitionManager.FindAsync(settingName, AbortToken).Returns((SettingDefinition?)null);

        // when
        var action = async () => await _sut.FindAsync(settingName, cancellationToken: AbortToken);

        // then
        await action.Should().ThrowExactlyAsync<ConflictException>();
    }

    [Fact]
    public async Task should_skip_to_specified_provider()
    {
        // given
        const string settingName = "TestSetting";
        var provider1 = new FakeSettingValueProvider { Name = "Provider1" };
        var provider2 = new FakeSettingValueProvider { Name = "Provider2" };
        var provider3 = new FakeSettingValueProvider { Name = "Provider3" };

        provider1.SetValue(settingName, "value1");
        provider2.SetValue(settingName, "value2");
        provider3.SetValue(settingName, "value3");

        var definition = new SettingDefinition(settingName);
        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider1, provider2, provider3]);

        // when
        var result = await _sut.FindAsync(settingName, providerName: "Provider2", cancellationToken: AbortToken);

        // then
        result.Should().Be("value2");
    }

    [Fact]
    public async Task should_use_provider_key_when_specified()
    {
        // given
        const string settingName = "TestSetting";
        const string providerKey = "tenant-123";
        var provider = new FakeSettingValueProvider { Name = "Provider1" };
        provider.SetValue(settingName, "keyed-value", providerKey);

        var definition = new SettingDefinition(settingName);
        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider]);

        // when
        var result = await _sut.FindAsync(
            settingName,
            providerName: "Provider1",
            providerKey: providerKey,
            cancellationToken: AbortToken
        );

        // then
        result.Should().Be("keyed-value");
    }

    [Fact]
    public async Task should_fallback_through_providers()
    {
        // given
        const string settingName = "TestSetting";
        var provider1 = new FakeSettingValueProvider { Name = "Provider1" };
        var provider2 = new FakeSettingValueProvider { Name = "Provider2" };
        var provider3 = new FakeSettingValueProvider { Name = "Provider3" };

        // Provider1 has no value, Provider2 has no value, Provider3 has value
        provider3.SetValue(settingName, "fallback-value");

        var definition = new SettingDefinition(settingName, isInherited: true);
        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider1, provider2, provider3]);

        // when
        var result = await _sut.FindAsync(settingName, fallback: true, cancellationToken: AbortToken);

        // then
        result.Should().Be("fallback-value");
    }

    [Fact]
    public async Task should_not_fallback_when_disabled()
    {
        // given
        const string settingName = "TestSetting";
        var provider1 = new FakeSettingValueProvider { Name = "Provider1" };
        var provider2 = new FakeSettingValueProvider { Name = "Provider2" };

        // Provider1 has no value, Provider2 has value
        provider2.SetValue(settingName, "provider2-value");

        var definition = new SettingDefinition(settingName, isInherited: true);
        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider1, provider2]);

        // when
        var result = await _sut.FindAsync(
            settingName,
            providerName: "Provider1",
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_decrypt_encrypted_settings()
    {
        // given
        const string settingName = "EncryptedSetting";
        const string encryptedValue = "encrypted-data";
        const string decryptedValue = "decrypted-data";

        var definition = new SettingDefinition(settingName, isEncrypted: true);
        var store = Substitute.For<ISettingValueStore>();
        store.GetOrDefaultAsync(settingName, "Store", null, AbortToken).Returns(encryptedValue);
        var provider = new FakeStoreSettingValueProvider(store);

        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider]);
        _encryptionService.Decrypt(definition, encryptedValue).Returns(decryptedValue);

        // when
        var result = await _sut.FindAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(decryptedValue);
        _encryptionService.Received(1).Decrypt(definition, encryptedValue);
    }

    [Fact]
    public async Task should_throw_when_setting_name_null()
    {
        // when
        var action = async () => await _sut.FindAsync(null!, cancellationToken: AbortToken);

        // then
        await action.Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    #endregion

    #region GetAllAsync (setting names)

    [Fact]
    public async Task should_get_all_for_setting_names()
    {
        // given
        var settings = new HashSet<string>(StringComparer.Ordinal) { "Setting1", "Setting2" };
        List<SettingDefinition> definitions =
        [
            new("Setting1", defaultValue: "default1"),
            new("Setting2", defaultValue: "default2"),
        ];

        var provider = new FakeSettingValueProvider { Name = "Provider1" };
        provider.SetValue("Setting1", "value1");
        provider.SetValue("Setting2", "value2");

        _definitionManager.GetAllAsync(AbortToken).Returns(definitions);
        _valueProviderManager.Providers.Returns([provider]);

        // when
        var result = await _sut.GetAllAsync(settings, AbortToken);

        // then
        result.Should().HaveCount(2);
        result["Setting1"].Value.Should().Be("value1");
        result["Setting2"].Value.Should().Be("value2");
    }

    #endregion

    #region GetAllAsync (provider name)

    [Fact]
    public async Task should_get_all_for_provider()
    {
        // given
        const string providerName = "Provider1";
        List<SettingDefinition> definitions = [new("Setting1", isInherited: true), new("Setting2", isInherited: true)];

        var provider = new FakeSettingValueProvider { Name = providerName };
        provider.SetValue("Setting1", "value1");
        provider.SetValue("Setting2", "value2");

        _definitionManager.GetAllAsync(AbortToken).Returns(definitions);
        _valueProviderManager.Providers.Returns([provider]);

        // when
        var result = await _sut.GetAllAsync(providerName, cancellationToken: AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(sv => sv.Name == "Setting1" && sv.Value == "value1");
        result.Should().Contain(sv => sv.Name == "Setting2" && sv.Value == "value2");
    }

    [Fact]
    public async Task should_throw_when_provider_name_null()
    {
        // when
        var action = async () => await _sut.GetAllAsync(null!, cancellationToken: AbortToken);

        // then
        await action.Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    #endregion

    #region SetAsync

    [Fact]
    public async Task should_set_value_for_provider()
    {
        // given
        const string settingName = "TestSetting";
        const string providerName = "Provider1";
        const string newValue = "new-value";

        var definition = new SettingDefinition(settingName);
        var provider = new FakeSettingValueProvider { Name = providerName };

        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider]);

        // when
        await _sut.SetAsync(settingName, newValue, providerName, null, cancellationToken: AbortToken);

        // then
        var result = await provider.GetOrDefaultAsync(definition, cancellationToken: AbortToken);
        result.Should().Be(newValue);
    }

    [Fact]
    public async Task should_encrypt_before_storing()
    {
        // given
        const string settingName = "EncryptedSetting";
        const string providerName = "Provider1";
        const string plainValue = "plain-value";
        const string encryptedValue = "encrypted-value";

        var definition = new SettingDefinition(settingName, isEncrypted: true);
        var provider = new FakeSettingValueProvider { Name = providerName };

        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider]);
        _encryptionService.Encrypt(definition, plainValue).Returns(encryptedValue);

        // when
        await _sut.SetAsync(settingName, plainValue, providerName, null, cancellationToken: AbortToken);

        // then
        _encryptionService.Received(1).Encrypt(definition, plainValue);
        var result = await provider.GetOrDefaultAsync(definition, cancellationToken: AbortToken);
        result.Should().Be(encryptedValue);
    }

    [Fact]
    public async Task should_clear_if_same_as_fallback()
    {
        // given
        const string settingName = "TestSetting";
        const string providerName = "Provider1";
        const string fallbackProviderName = "Provider2";
        const string fallbackValue = "fallback-value";

        var definition = new SettingDefinition(settingName, isInherited: true);
        var provider1 = new FakeSettingValueProvider { Name = providerName };
        var provider2 = new FakeSettingValueProvider { Name = fallbackProviderName };

        // Set up provider1 with existing value
        provider1.SetValue(settingName, "old-value");
        // Set up provider2 with fallback value
        provider2.SetValue(settingName, fallbackValue);

        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([provider1, provider2]);

        // when - setting value same as fallback should clear it
        await _sut.SetAsync(settingName, fallbackValue, providerName, null, cancellationToken: AbortToken);

        // then - value should be cleared
        var result = await provider1.GetOrDefaultAsync(definition, cancellationToken: AbortToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_provider_readonly()
    {
        // given
        const string settingName = "TestSetting";
        const string providerName = "ReadOnlyProvider";

        var definition = new SettingDefinition(settingName);

        // Use a read-only provider (ISettingValueReadProvider, not ISettingValueProvider)
        var readOnlyProvider = Substitute.For<ISettingValueReadProvider>();
        readOnlyProvider.Name.Returns(providerName);

        _definitionManager.FindAsync(settingName, AbortToken).Returns(definition);
        _valueProviderManager.Providers.Returns([readOnlyProvider]);

        // when
        var action = async () =>
            await _sut.SetAsync(settingName, "value", providerName, null, cancellationToken: AbortToken);

        // then
        await action.Should().ThrowExactlyAsync<ConflictException>();
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task should_delete_all_provider_values()
    {
        // given
        const string providerName = "Provider1";
        const string providerKey = "tenant-123";
        List<SettingValue> settingValues = [new("Setting1", "value1"), new("Setting2", "value2")];

        _valueStore.GetAllProviderValuesAsync(providerName, providerKey, AbortToken).Returns(settingValues);

        // when
        await _sut.DeleteAsync(providerName, providerKey, AbortToken);

        // then
        await _valueStore.Received(1).DeleteAsync("Setting1", providerName, providerKey, AbortToken);
        await _valueStore.Received(1).DeleteAsync("Setting2", providerName, providerKey, AbortToken);
    }

    #endregion
}
