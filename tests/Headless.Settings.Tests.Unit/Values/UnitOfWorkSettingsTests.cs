// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests.Values;

public sealed class UnitOfWorkSettingsTests : TestBase
{
    private readonly IUnitOfWorkSettings _feature = Substitute.For<IUnitOfWorkSettings>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();

    public UnitOfWorkSettingsTests()
    {
        _unit
            .GetOrAdd(
                Arg.Any<IUnitOfWorkSettings>(),
                Arg.Any<Func<IUnitOfWork, IUnitOfWorkSettings, UnitOfWorkSettings>>()
            )
            .Returns(call =>
                call.Arg<Func<IUnitOfWork, IUnitOfWorkSettings, UnitOfWorkSettings>>()(
                    _unit,
                    call.Arg<IUnitOfWorkSettings>()
                )
            );
    }

    [Fact]
    public async Task should_bind_every_write_to_the_unit_it_was_read_from()
    {
        // given
        _unit.GetFeature<IUnitOfWorkSettings>().Returns(_feature);
        var settings = _unit.Settings;
        var batch = new Dictionary<string, string?>(StringComparer.Ordinal) { ["App.Locale"] = "ar" };

        // when
        await settings.SetAsync("App.Theme", "dark", "Global", null, cancellationToken: AbortToken);
        await settings.SetAsync("App.Count", 3, "Global", null, cancellationToken: AbortToken);
        await settings.SetAsync(batch, "Tenant", "t1", forceToSet: true, AbortToken);
        await settings.SetGlobalAsync("App.Mode", "on", AbortToken);
        await settings.SetForTenantAsync("t1", "App.Theme", "light", cancellationToken: AbortToken);
        await settings.SetForUserAsync("u1", "App.Theme", "blue", cancellationToken: AbortToken);
        await settings.DeleteAsync("User", "u1", AbortToken);

        // then
        await _feature.Received(1).SetAsync(_unit, _One("App.Theme", "dark"), "Global", null, false, AbortToken);
        await _feature.Received(1).SetAsync(_unit, _One("App.Count", "3"), "Global", null, false, AbortToken);
        await _feature.Received(1).SetAsync(_unit, batch, "Tenant", "t1", true, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, _One("App.Mode", "on"), SettingValueProviderNames.Global, null, false, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, _One("App.Theme", "light"), SettingValueProviderNames.Tenant, "t1", false, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, _One("App.Theme", "blue"), SettingValueProviderNames.User, "u1", false, AbortToken);
        await _feature.Received(1).DeleteAsync(_unit, "User", "u1", AbortToken);
    }

    [Fact]
    public void should_name_the_registration_when_settings_are_not_registered()
    {
        // given
        _unit.GetFeature<IUnitOfWorkSettings>().Returns((IUnitOfWorkSettings?)null);

        // when
        var act = () => _unit.Settings;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessSettings*");
    }

    private static IReadOnlyDictionary<string, string?> _One(string name, string value)
    {
        return Arg.Is<IReadOnlyDictionary<string, string?>>(x =>
            x.Count == 1 && x.ContainsKey(name) && x[name] == value
        );
    }
}
