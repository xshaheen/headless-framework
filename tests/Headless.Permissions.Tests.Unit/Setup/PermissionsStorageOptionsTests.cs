// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class PermissionsStorageOptionsTests
{
    [Theory]
    [InlineData("", "PermissionGrants", "PermissionDefinitions", "PermissionGroupDefinitions")]
    [InlineData("permissions", "", "PermissionDefinitions", "PermissionGroupDefinitions")]
    [InlineData("permissions", "PermissionGrants", "", "PermissionGroupDefinitions")]
    [InlineData("permissions", "PermissionGrants", "PermissionDefinitions", "")]
    [InlineData("   ", "PermissionGrants", "PermissionDefinitions", "PermissionGroupDefinitions")]
    [InlineData("permissions", "   ", "PermissionDefinitions", "PermissionGroupDefinitions")]
    [InlineData("permissions", "PermissionGrants", "   ", "PermissionGroupDefinitions")]
    [InlineData("permissions", "PermissionGrants", "PermissionDefinitions", "   ")]
    public void should_reject_storage_options_when_any_field_is_blank(
        string schema,
        string grantsTable,
        string definitionsTable,
        string groupDefinitionsTable
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddPermissionsManagementDbContextStorage<PermissionsDbContext>(options =>
        {
            options.Schema = schema;
            options.PermissionGrantsTableName = grantsTable;
            options.PermissionDefinitionsTableName = definitionsTable;
            options.PermissionGroupDefinitionsTableName = groupDefinitionsTable;
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PermissionsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_accept_storage_options_when_all_fields_are_non_blank()
    {
        // given
        var services = new ServiceCollection();
        services.AddPermissionsManagementDbContextStorage<PermissionsDbContext>(options =>
        {
            options.Schema = "custom_permissions";
            options.PermissionGrantsTableName = "tbl_permission_grants";
            options.PermissionDefinitionsTableName = "tbl_permission_definitions";
            options.PermissionGroupDefinitionsTableName = "tbl_permission_group_definitions";
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PermissionsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        var resolved = act.Should().NotThrow().Subject;
        resolved.Schema.Should().Be("custom_permissions");
        resolved.PermissionGrantsTableName.Should().Be("tbl_permission_grants");
        resolved.PermissionDefinitionsTableName.Should().Be("tbl_permission_definitions");
        resolved.PermissionGroupDefinitionsTableName.Should().Be("tbl_permission_group_definitions");
    }

    [Fact]
    public void should_accept_storage_options_when_left_at_defaults()
    {
        // given
        var services = new ServiceCollection();
        services.AddPermissionsManagementDbContextStorage<PermissionsDbContext>();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PermissionsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        var resolved = act.Should().NotThrow().Subject;
        resolved.Schema.Should().Be("permissions");
        resolved.PermissionGrantsTableName.Should().Be("PermissionGrants");
        resolved.PermissionDefinitionsTableName.Should().Be("PermissionDefinitions");
        resolved.PermissionGroupDefinitionsTableName.Should().Be("PermissionGroupDefinitions");
    }
}
