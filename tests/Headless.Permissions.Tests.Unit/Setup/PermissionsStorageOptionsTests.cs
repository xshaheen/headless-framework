// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
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
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = schema;
                options.PermissionGrantsTableName = grantsTable;
                options.PermissionDefinitionsTableName = definitionsTable;
                options.PermissionGroupDefinitionsTableName = groupDefinitionsTable;
            });
            setup.UseEntityFramework<OptionsTestDbContext>();
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
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = "custom_permissions";
                options.PermissionGrantsTableName = "tbl_permission_grants";
                options.PermissionDefinitionsTableName = "tbl_permission_definitions";
                options.PermissionGroupDefinitionsTableName = "tbl_permission_group_definitions";
            });
            setup.UseEntityFramework<OptionsTestDbContext>();
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
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<OptionsTestDbContext>());
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

    [Fact]
    public void should_reject_multiple_storage_provider_registrations()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        // when
        var action = () => services.AddHeadlessPermissions(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*exactly one storage provider*");
    }

    [Fact]
    public void should_apply_permission_model_configuration_when_entities_are_already_discovered()
    {
        // given
        var storageOptions = new PermissionsStorageOptions
        {
            Schema = "custom_permissions",
            PermissionGrantsTableName = "custom_permission_grants",
            PermissionDefinitionsTableName = "custom_permission_definitions",
            PermissionGroupDefinitionsTableName = "custom_permission_group_definitions",
        };
        using var context = new ExistingPermissionsEntityDbContext(
            new DbContextOptionsBuilder<ExistingPermissionsEntityDbContext>().UseSqlite("DataSource=:memory:").Options,
            storageOptions
        );

        // when
        var grantEntity = context.Model.FindEntityType(typeof(PermissionGrantRecord));
        var definitionEntity = context.Model.FindEntityType(typeof(PermissionDefinitionRecord));
        var groupEntity = context.Model.FindEntityType(typeof(PermissionGroupDefinitionRecord));

        // then
        grantEntity.Should().NotBeNull();
        grantEntity!.GetSchema().Should().Be("custom_permissions");
        grantEntity.GetTableName().Should().Be("custom_permission_grants");
        definitionEntity.Should().NotBeNull();
        definitionEntity!.GetTableName().Should().Be("custom_permission_definitions");
        groupEntity.Should().NotBeNull();
        groupEntity!.GetTableName().Should().Be("custom_permission_group_definitions");
    }

    private sealed class OptionsTestDbContext(DbContextOptions<OptionsTestDbContext> options) : DbContext(options);

    private sealed class ExistingPermissionsEntityDbContext(
        DbContextOptions<ExistingPermissionsEntityDbContext> options,
        PermissionsStorageOptions storageOptions
    ) : DbContext(options)
    {
        public DbSet<PermissionGrantRecord> PermissionGrants => Set<PermissionGrantRecord>();

        public DbSet<PermissionDefinitionRecord> PermissionDefinitions => Set<PermissionDefinitionRecord>();

        public DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions =>
            Set<PermissionGroupDefinitionRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessPermissions(storageOptions);
        }
    }
}
