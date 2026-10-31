// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class ConfigureManagementTests
{
    [Fact]
    public void should_apply_management_options_set_via_configure_management()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureManagement(options => options.CrossApplicationsCommonLockKey = "custom:settings_lock");
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<SettingManagementOptions>>().Value;

        // then
        options.CrossApplicationsCommonLockKey.Should().Be("custom:settings_lock");
    }

    [Fact]
    public void should_apply_management_options_set_via_configure_management_with_service_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureManagement(
                (options, _) => options.CrossApplicationsCommonLockKey = "custom:settings_lock_sp"
            );
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<SettingManagementOptions>>().Value;

        // then
        options.CrossApplicationsCommonLockKey.Should().Be("custom:settings_lock_sp");
    }

    [Fact]
    public void should_still_validate_management_options_when_configured_invalid()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureManagement(options => options.CrossApplicationsCommonLockKey = ""); // NotEmpty rule
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<SettingManagementOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    private sealed class OptionsTestDbContext(DbContextOptions<OptionsTestDbContext> options) : DbContext(options);
}
