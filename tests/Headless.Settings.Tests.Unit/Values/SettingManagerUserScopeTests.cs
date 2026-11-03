// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.MultiTenancy;
using Headless.Settings;
using Headless.Settings.Resources;
using Headless.Testing.Tests;
using Tests.Fakes;

namespace Tests.Values;

/// <summary>
/// A write for a named user compares the value against the fallback that user really inherits. From host scope the
/// manager cannot tell the user's tenant, so it keeps the value rather than compare it with the host's chain.
/// </summary>
public sealed class SettingManagerUserScopeTests : TestBase
{
    private const string _Setting = "Theme";
    private const string _Tenant = "acme";
    private const string _UserId = "user-1";

    private readonly SettingDefinition _definition = new(_Setting);
    private readonly ISettingValueStore _store = Substitute.For<ISettingValueStore>();
    private readonly CurrentTenant _currentTenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly FakeSettingValueProvider _user = new() { Name = SettingValueProviderNames.User };
    private readonly FakeSettingValueProvider _global = new() { Name = SettingValueProviderNames.Global };
    private readonly SettingManager _sut;

    public SettingManagerUserScopeTests()
    {
        // acme overrides the theme to "light"; the global value is "dark". NSubstitute would answer "" for any other
        // scope, which reads as a stored value.
        _store
            .GetOrDefaultAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _store
            .GetOrDefaultAsync(_Setting, SettingValueProviderNames.Tenant, _Tenant, Arg.Any<CancellationToken>())
            .Returns("light");
        _global.SetValue(_Setting, "dark");

        var definitions = Substitute.For<ISettingDefinitionManager>();
        definitions.FindAsync(_Setting, Arg.Any<CancellationToken>()).Returns(_definition);

        var providers = Substitute.For<ISettingValueProviderManager>();
        providers.Providers.Returns([_user, new TenantSettingValueProvider(_store, _currentTenant), _global]);

        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns("host");

        _sut = new SettingManager(
            definitions,
            _store,
            providers,
            Substitute.For<ISettingEncryptionService>(),
            new DefaultSettingErrorsDescriptor(),
            hostIdentity,
            _currentTenant
        );
    }

    [Fact]
    public async Task should_keep_a_host_write_for_a_user_whose_tenant_it_cannot_see()
    {
        // when a host administrator sets the user's theme to "dark", which equals only the host's fallback
        await _sut.SetAsync(_Setting, "dark", SettingValueProviderNames.User, _UserId, cancellationToken: AbortToken);

        // then it is stored: under acme the user would otherwise inherit acme's "light"
        (await _user.GetOrDefaultAsync(_definition, _UserId, AbortToken))
            .Should()
            .Be("dark");
    }

    [Fact]
    public async Task should_clear_a_write_equal_to_the_users_tenant_fallback_inside_that_tenant()
    {
        // given the write runs inside the user's tenant
        using var tenant = _currentTenant.Change(_Tenant);

        // when the value equals acme's override
        await _sut.SetAsync(_Setting, "light", SettingValueProviderNames.User, _UserId, cancellationToken: AbortToken);

        // then the user keeps inheriting it
        (await _user.GetOrDefaultAsync(_definition, _UserId, AbortToken))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task should_clear_a_current_user_write_equal_to_the_fallback()
    {
        // when the current user (no explicit key) writes the global value from host scope
        await _sut.SetAsync(
            _Setting,
            "dark",
            SettingValueProviderNames.User,
            providerKey: null,
            cancellationToken: AbortToken
        );

        // then the comparison runs as before and nothing is pinned
        (await _user.GetOrDefaultAsync(_definition, providerKey: null, AbortToken))
            .Should()
            .BeNull();
    }
}
