// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Primitives;
using Headless.Settings.Models;
using Headless.Settings.ValueProviders;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.ValueProviders;

public sealed class UserSettingValueProviderTests : TestBase
{
    private readonly ISettingValueStore _store = Substitute.For<ISettingValueStore>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly UserSettingValueProvider _sut;

    public UserSettingValueProviderTests()
    {
        _sut = new UserSettingValueProvider(_store, _user);
    }

    [Fact]
    public async Task should_read_from_store_with_user_key()
    {
        // given
        var setting = new SettingDefinition("user.setting");
        var userId = new UserId("user-123");
        _user.UserId.Returns(userId);
        _store
            .GetOrDefaultAsync("user.setting", SettingValueProviderNames.User, userId.ToString(), AbortToken)
            .Returns("user-value");

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().Be("user-value");
    }

    [Fact]
    public async Task should_use_current_user_id()
    {
        // given
        var setting = new SettingDefinition("user.setting");
        var userId = new UserId("current-user-id");
        _user.UserId.Returns(userId);

        // when
        await _sut.GetOrDefaultAsync(setting, providerKey: null, AbortToken);

        // then
        await _store
            .Received(1)
            .GetOrDefaultAsync("user.setting", SettingValueProviderNames.User, userId.ToString(), AbortToken);
    }

    [Fact]
    public async Task should_use_provided_key_over_current()
    {
        // given
        var setting = new SettingDefinition("user.setting");
        var userId = new UserId("current-user-id");
        _user.UserId.Returns(userId);

        // when
        await _sut.GetOrDefaultAsync(setting, providerKey: "explicit-user-id", AbortToken);

        // then
        await _store
            .Received(1)
            .GetOrDefaultAsync("user.setting", SettingValueProviderNames.User, "explicit-user-id", AbortToken);
    }

    [Fact]
    public async Task should_return_null_when_no_user()
    {
        // given
        var setting = new SettingDefinition("user.setting");
        _user.UserId.Returns((UserId?)null);
        _store
            .GetOrDefaultAsync("user.setting", SettingValueProviderNames.User, null, AbortToken)
            .Returns((string?)null);

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_write_to_store_with_user_key()
    {
        // given
        var setting = new SettingDefinition("user.setting");
        var userId = new UserId("user-456");
        _user.UserId.Returns(userId);

        // when
        await _sut.SetAsync(setting, "new-user-value", providerKey: null, AbortToken);

        // then
        await _store
            .Received(1)
            .SetAsync("user.setting", "new-user-value", SettingValueProviderNames.User, userId.ToString(), AbortToken);
    }

    [Fact]
    public async Task should_clear_from_store()
    {
        // given
        var setting = new SettingDefinition("user.setting");
        var userId = new UserId("user-789");
        _user.UserId.Returns(userId);

        // when
        await _sut.ClearAsync(setting, providerKey: null, AbortToken);

        // then
        await _store
            .Received(1)
            .DeleteAsync("user.setting", SettingValueProviderNames.User, userId.ToString(), AbortToken);
    }

    [Fact]
    public void should_return_provider_name()
    {
        // when & then
        _sut.Name.Should().Be(SettingValueProviderNames.User);
        UserSettingValueProvider.ProviderName.Should().Be("User");
    }
}
