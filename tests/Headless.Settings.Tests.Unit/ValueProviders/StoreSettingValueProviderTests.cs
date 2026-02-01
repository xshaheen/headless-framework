// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Headless.Settings.ValueProviders;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.ValueProviders;

public sealed class StoreSettingValueProviderTests : TestBase
{
    private readonly ISettingValueStore _store = Substitute.For<ISettingValueStore>();
    private readonly TestStoreSettingValueProvider _sut;

    public StoreSettingValueProviderTests()
    {
        _sut = new TestStoreSettingValueProvider(_store);
    }

    [Fact]
    public async Task should_read_from_value_store()
    {
        // given
        var setting = new SettingDefinition("test.setting");
        _store.GetOrDefaultAsync("test.setting", "Test", "key-123", AbortToken).Returns("stored-value");

        // when
        var result = await _sut.GetOrDefaultAsync(setting, providerKey: "key-123", AbortToken);

        // then
        result.Should().Be("stored-value");
    }

    [Fact]
    public async Task should_write_to_value_store()
    {
        // given
        var setting = new SettingDefinition("test.setting");

        // when
        await _sut.SetAsync(setting, "new-value", providerKey: "key-456", AbortToken);

        // then
        await _store.Received(1).SetAsync("test.setting", "new-value", "Test", "key-456", AbortToken);
    }

    [Fact]
    public async Task should_clear_from_value_store()
    {
        // given
        var setting = new SettingDefinition("test.setting");

        // when
        await _sut.ClearAsync(setting, providerKey: "key-789", AbortToken);

        // then
        await _store.Received(1).DeleteAsync("test.setting", "Test", "key-789", AbortToken);
    }

    [Fact]
    public async Task should_get_all_from_store()
    {
        // given
        var settings = new[] { new SettingDefinition("setting1"), new SettingDefinition("setting2") };

        _store
            .GetAllAsync(
                Arg.Is<HashSet<string>>(s => s.Contains("setting1") && s.Contains("setting2")),
                "Test",
                "key-abc",
                AbortToken
            )
            .Returns([new SettingValue("setting1", "value1"), new SettingValue("setting2", "value2")]);

        // when
        var result = await _sut.GetAllAsync(settings, providerKey: "key-abc", AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(v => v.Name == "setting1" && v.Value == "value1");
        result.Should().Contain(v => v.Name == "setting2" && v.Value == "value2");
    }

    [Fact]
    public void should_return_provider_name()
    {
        // when & then
        _sut.Name.Should().Be("Test");
    }

    /// <summary>Test implementation that exposes the abstract class behavior.</summary>
    private sealed class TestStoreSettingValueProvider(ISettingValueStore store) : StoreSettingValueProvider(store)
    {
        public override string Name => "Test";
    }
}
