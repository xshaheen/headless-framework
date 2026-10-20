// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public void should_register_the_definitions_seeder_without_seeding_at_startup()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        // then - seeding is opt-in: the seeder is registered, but the startup seed runner that
        // AddDeployStepsOnStartup adds is not, so nothing seeds when the host starts
        services.Should().Contain(d => d.ServiceType == typeof(ISeeder));
        using var provider = services.BuildServiceProvider();
        provider
            .GetServices<IHostedService>()
            .Select(static s => s.GetType().Name)
            .Should()
            .NotContain("SeedStartupService");
    }

    private sealed class OptionsTestDbContext(DbContextOptions<OptionsTestDbContext> options) : DbContext(options);
}
