// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Values;

public sealed class TenantSettingManagerExtensionsTests : TestBase
{
    private readonly ISettingManager _settingManager;

    public TenantSettingManagerExtensionsTests()
    {
        _settingManager = Substitute.For<ISettingManager>();
    }

    #region IsTrueForTenantAsync

    [Fact]
    public async Task should_call_with_tenant_provider_and_tenant_id()
    {
        // given
        const string tenantId = "tenant-123";
        const string settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Tenant, tenantId, true, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueForTenantAsync(tenantId, settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Tenant, tenantId, true, AbortToken);
    }

    [Fact]
    public async Task should_pass_fallback_for_is_true_tenant()
    {
        // given
        const string tenantId = "tenant-123";
        const string settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Tenant, tenantId, false, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueForTenantAsync(
            tenantId,
            settingName,
            fallback: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
    }

    #endregion

    #region IsTrueForCurrentTenantAsync

    [Fact]
    public async Task should_call_with_tenant_provider_and_null_key_for_current()
    {
        // given
        const string settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Tenant, null, true, AbortToken)
            .Returns("true");

        // when
        var result = await _settingManager.IsTrueForCurrentTenantAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
        await _settingManager
            .Received(1)
            .FindAsync(settingName, SettingValueProviderNames.Tenant, null, true, AbortToken);
    }

    #endregion

    #region IsFalseForTenantAsync

    [Fact]
    public async Task should_call_with_tenant_provider_for_is_false()
    {
        // given
        const string tenantId = "tenant-456";
        const string settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Tenant, tenantId, true, AbortToken)
            .Returns("false");

        // when
        var result = await _settingManager.IsFalseForTenantAsync(tenantId, settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    #endregion

    #region IsFalseForCurrentTenantAsync

    [Fact]
    public async Task should_call_with_tenant_provider_and_null_key_for_is_false_current()
    {
        // given
        const string settingName = "TestSetting";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Tenant, null, true, AbortToken)
            .Returns("false");

        // when
        var result = await _settingManager.IsFalseForCurrentTenantAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().BeTrue();
    }

    #endregion

    #region FindForTenantAsync<T>

    [Fact]
    public async Task should_find_typed_from_tenant_provider()
    {
        // given
        const string tenantId = "tenant-123";
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "tenant-test" };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Tenant, tenantId, true, AbortToken)
            .Returns(json);

        // when
        var result = await _settingManager.FindForTenantAsync<TestSettings>(
            tenantId,
            settingName,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be("tenant-test");
    }

    #endregion

    #region FindForCurrentTenantAsync<T>

    [Fact]
    public async Task should_find_typed_from_current_tenant()
    {
        // given
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "current-tenant-test" };
        var json = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        _settingManager.FindAsync(settingName, SettingValueProviderNames.Tenant, null, true, AbortToken).Returns(json);

        // when
        var result = await _settingManager.FindForCurrentTenantAsync<TestSettings>(
            settingName,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be("current-tenant-test");
    }

    #endregion

    #region FindForTenantAsync (string)

    [Fact]
    public async Task should_find_string_from_tenant_provider()
    {
        // given
        const string tenantId = "tenant-789";
        const string settingName = "TestSetting";
        const string expectedValue = "tenant-value";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Tenant, tenantId, true, AbortToken)
            .Returns(expectedValue);

        // when
        var result = await _settingManager.FindForTenantAsync(tenantId, settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
    }

    #endregion

    #region FindForCurrentTenantAsync (string)

    [Fact]
    public async Task should_find_string_from_current_tenant()
    {
        // given
        const string settingName = "TestSetting";
        const string expectedValue = "current-tenant-value";

        _settingManager
            .FindAsync(settingName, SettingValueProviderNames.Tenant, null, true, AbortToken)
            .Returns(expectedValue);

        // when
        var result = await _settingManager.FindForCurrentTenantAsync(settingName, cancellationToken: AbortToken);

        // then
        result.Should().Be(expectedValue);
    }

    #endregion

    #region GetAllForTenantAsync

    [Fact]
    public async Task should_get_all_from_tenant_provider()
    {
        // given
        const string tenantId = "tenant-123";
        List<SettingValue> expectedValues = [new("Setting1", "value1"), new("Setting2", "value2")];

        _settingManager
            .GetAllAsync(SettingValueProviderNames.Tenant, tenantId, true, AbortToken)
            .Returns(expectedValues);

        // when
        var result = await _settingManager.GetAllForTenantAsync(tenantId, cancellationToken: AbortToken);

        // then
        result.Should().BeEquivalentTo(expectedValues);
    }

    #endregion

    #region GetAllForCurrentTenantAsync

    [Fact]
    public async Task should_get_all_from_current_tenant()
    {
        // given
        List<SettingValue> expectedValues = [new("Setting1", "value1")];

        _settingManager.GetAllAsync(SettingValueProviderNames.Tenant, null, true, AbortToken).Returns(expectedValues);

        // when
        var result = await _settingManager.GetAllForCurrentTenantAsync(cancellationToken: AbortToken);

        // then
        result.Should().BeEquivalentTo(expectedValues);
    }

    #endregion

    #region SetForTenantAsync (string)

    [Fact]
    public async Task should_set_value_for_tenant_provider()
    {
        // given
        const string tenantId = "tenant-123";
        const string settingName = "TestSetting";
        const string value = "new-tenant-value";

        // when
        await _settingManager.SetForTenantAsync(tenantId, settingName, value, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.Tenant, tenantId, false, AbortToken);
    }

    [Fact]
    public async Task should_pass_force_to_set_for_tenant()
    {
        // given
        const string tenantId = "tenant-123";
        const string settingName = "TestSetting";
        const string value = "forced-value";

        // when
        await _settingManager.SetForTenantAsync(
            tenantId,
            settingName,
            value,
            forceToSet: true,
            cancellationToken: AbortToken
        );

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.Tenant, tenantId, true, AbortToken);
    }

    #endregion

    #region SetForCurrentTenantAsync (string)

    [Fact]
    public async Task should_set_value_for_current_tenant()
    {
        // given
        const string settingName = "TestSetting";
        const string value = "current-tenant-value";

        // when
        await _settingManager.SetForCurrentTenantAsync(settingName, value, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.Tenant, null, false, AbortToken);
    }

    #endregion

    #region SetForTenantOrGlobalAsync (string)

    [Fact]
    public async Task should_set_for_tenant_when_tenant_id_provided()
    {
        // given
        const string tenantId = "tenant-123";
        const string settingName = "TestSetting";
        const string value = "tenant-or-global-value";

        // when
        await _settingManager.SetForTenantOrGlobalAsync(tenantId, settingName, value, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.Tenant, tenantId, false, AbortToken);
    }

    [Fact]
    public async Task should_set_for_global_when_tenant_id_is_null()
    {
        // given
        const string settingName = "TestSetting";
        const string value = "global-fallback-value";

        // when
        await _settingManager.SetForTenantOrGlobalAsync(null, settingName, value, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, value, SettingValueProviderNames.Global, null, false, AbortToken);
    }

    #endregion

    #region SetForTenantAsync<T>

    [Fact]
    public async Task should_set_typed_value_for_tenant()
    {
        // given
        const string tenantId = "tenant-123";
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "typed-tenant-value" };
        var expectedJson = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        // when
        await _settingManager.SetForTenantAsync(tenantId, settingName, testObj, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, expectedJson, SettingValueProviderNames.Tenant, tenantId, false, AbortToken);
    }

    #endregion

    #region SetForCurrentTenantAsync<T>

    [Fact]
    public async Task should_set_typed_value_for_current_tenant()
    {
        // given
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "typed-current-tenant-value" };
        var expectedJson = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        // when
        await _settingManager.SetForCurrentTenantAsync(settingName, testObj, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, expectedJson, SettingValueProviderNames.Tenant, null, false, AbortToken);
    }

    #endregion

    #region SetForTenantOrGlobalAsync<T>

    [Fact]
    public async Task should_set_typed_for_tenant_when_tenant_id_provided()
    {
        // given
        const string tenantId = "tenant-456";
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "typed-tenant-or-global" };
        var expectedJson = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        // when
        await _settingManager.SetForTenantOrGlobalAsync(tenantId, settingName, testObj, cancellationToken: AbortToken);

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, expectedJson, SettingValueProviderNames.Tenant, tenantId, false, AbortToken);
    }

    [Fact]
    public async Task should_set_typed_for_global_when_tenant_id_is_null()
    {
        // given
        const string settingName = "TestSetting";
        var testObj = new TestSettings { Value = "typed-global-fallback" };
        var expectedJson = JsonSerializer.Serialize(testObj, JsonConstants.DefaultInternalJsonOptions);

        // when
        await _settingManager.SetForTenantOrGlobalAsync<TestSettings>(
            null,
            settingName,
            testObj,
            cancellationToken: AbortToken
        );

        // then
        await _settingManager
            .Received(1)
            .SetAsync(settingName, expectedJson, SettingValueProviderNames.Global, null, false, AbortToken);
    }

    #endregion

    private sealed class TestSettings
    {
        public string Value { get; init; } = "";
    }
}
