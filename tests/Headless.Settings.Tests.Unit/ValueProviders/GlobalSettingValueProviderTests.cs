// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Headless.Settings.ValueProviders;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.ValueProviders;

public sealed class GlobalSettingValueProviderTests : TestBase
{
    private readonly ISettingValueStore _store = Substitute.For<ISettingValueStore>();
    private readonly GlobalSettingValueProvider _sut;

    public GlobalSettingValueProviderTests()
    {
        _sut = new GlobalSettingValueProvider(_store);
    }

    [Fact]
    public async Task should_read_from_store_with_null_key()
    {
        // given
        var setting = new SettingDefinition("global.setting");
        _store
            .GetOrDefaultAsync("global.setting", SettingValueProviderNames.Global, null, AbortToken)
            .Returns("global-value");

        // when
        var result = await _sut.GetOrDefaultAsync(setting, providerKey: "ignored-key", AbortToken);

        // then
        result.Should().Be("global-value");
        await _store
            .Received(1)
            .GetOrDefaultAsync("global.setting", SettingValueProviderNames.Global, null, AbortToken);
    }

    [Fact]
    public async Task should_write_to_store_with_null_key()
    {
        // given
        var setting = new SettingDefinition("global.setting");

        // when
        await _sut.SetAsync(setting, "new-value", providerKey: "ignored-key", AbortToken);

        // then
        await _store
            .Received(1)
            .SetAsync("global.setting", "new-value", SettingValueProviderNames.Global, null, AbortToken);
    }

    [Fact]
    public async Task should_clear_from_store()
    {
        // given
        var setting = new SettingDefinition("global.setting");

        // when
        await _sut.ClearAsync(setting, providerKey: "ignored-key", AbortToken);

        // then
        await _store.Received(1).DeleteAsync("global.setting", SettingValueProviderNames.Global, null, AbortToken);
    }

    [Fact]
    public void should_return_provider_name()
    {
        // when & then
        _sut.Name.Should().Be(SettingValueProviderNames.Global);
        GlobalSettingValueProvider.ProviderName.Should().Be("Global");
    }
}
