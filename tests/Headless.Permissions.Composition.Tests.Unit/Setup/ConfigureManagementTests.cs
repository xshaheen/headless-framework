// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions;
using Headless.Permissions.Models;
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
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureManagement(options => options.CrossApplicationsCommonLockKey = "custom:permissions_lock");
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PermissionManagementOptions>>().Value;

        // then
        options.CrossApplicationsCommonLockKey.Should().Be("custom:permissions_lock");
    }

    [Fact]
    public void should_apply_management_options_set_via_configure_management_with_service_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureManagement(
                (options, _) => options.CrossApplicationsCommonLockKey = "custom:permissions_lock_sp"
            );
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PermissionManagementOptions>>().Value;

        // then
        options.CrossApplicationsCommonLockKey.Should().Be("custom:permissions_lock_sp");
    }

    [Fact]
    public void should_still_validate_management_options_when_configured_invalid()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureManagement(options => options.CrossApplicationsCommonLockKey = ""); // NotEmpty rule
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PermissionManagementOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void should_reject_policy_name_prefix_when_empty_or_whitespace(string prefix)
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureManagement(options => options.PolicyNamePrefix = prefix);
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PermissionManagementOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("permission:")]
    public void should_accept_policy_name_prefix_when_unset_or_non_blank(string? prefix)
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureManagement(options => options.PolicyNamePrefix = prefix);
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<PermissionManagementOptions>>().Value;

        // then
        options.PolicyNamePrefix.Should().Be(prefix);
    }

    [Fact]
    public void should_register_startup_initializer_by_default()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        // then
        services.Should().Contain(d => d.ServiceType == typeof(IInitializer));
    }

    [Fact]
    public void should_not_register_startup_initializer_when_disabled()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessPermissions(setup =>
        {
            setup.DisableStartupInitialization();
            setup.UseEntityFramework<OptionsTestDbContext>();
        });

        // then — the seeder is the only IInitializer the package registers; the other hosted services stay.
        services.Should().NotContain(d => d.ServiceType == typeof(IInitializer));
        services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<PermissionManagementOptions>));
    }

    private sealed class OptionsTestDbContext(DbContextOptions<OptionsTestDbContext> options) : DbContext(options);
}
