// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Storage.EntityFramework;
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
    public void should_validate_storage_option_fields(
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
}
