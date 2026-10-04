// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions;
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
                options.PermissionGrantsTableName = "tbl_grants";
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
        resolved.PermissionGrantsTableName.Should().Be("tbl_grants");
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
        resolved.Schema.Should().Be("headless");
        resolved.PermissionGrantsTableName.Should().BeNull();
        resolved.PermissionDefinitionsTableName.Should().BeNull();
        resolved.PermissionGroupDefinitionsTableName.Should().BeNull();
    }

    [Theory]
    [InlineData(
        StorageNamingStyle.PascalCase,
        "PermissionGrants",
        "PermissionDefinitions",
        "PermissionGroupDefinitions"
    )]
    [InlineData(
        StorageNamingStyle.SnakeCase,
        "permission_grants",
        "permission_definitions",
        "permission_group_definitions"
    )]
    public void should_resolve_default_table_names_in_the_database_naming_style(
        StorageNamingStyle style,
        string grantsTable,
        string definitionsTable,
        string groupDefinitionsTable
    )
    {
        // given
        var options = new PermissionsStorageOptions();

        // when / then
        options.ResolvePermissionGrantsTableName(style).Should().Be(grantsTable);
        options.ResolvePermissionDefinitionsTableName(style).Should().Be(definitionsTable);
        options.ResolvePermissionGroupDefinitionsTableName(style).Should().Be(groupDefinitionsTable);
    }

    [Fact]
    public void should_resolve_configured_table_names_verbatim_in_every_naming_style()
    {
        // given
        var options = new PermissionsStorageOptions
        {
            PermissionGrantsTableName = "MyGrants",
            PermissionDefinitionsTableName = "MyDefinitions",
            PermissionGroupDefinitionsTableName = "MyGroups",
        };

        // when / then
        options.ResolvePermissionGrantsTableName(StorageNamingStyle.SnakeCase).Should().Be("MyGrants");
        options.ResolvePermissionDefinitionsTableName(StorageNamingStyle.SnakeCase).Should().Be("MyDefinitions");
        options.ResolvePermissionGroupDefinitionsTableName(StorageNamingStyle.PascalCase).Should().Be("MyGroups");
    }

    [Fact]
    public void should_accept_the_snake_case_defaults_when_configured_explicitly()
    {
        // given — the conventional names themselves must fit, or the defaults' derived names would be truncated
        var services = _ServicesWithTableNames(
            "permission_grants",
            "permission_definitions",
            "permission_group_definitions"
        );
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PermissionsStorageOptions>>().Value;

        // then
        act.Should().NotThrow();
    }

    // The longest derived PostgreSQL names are ix_{grants}_tenant_id_name_provider_name_provider_key (45 bytes besides
    // the table), ix_{definitions}_group_name (14), and ix_{groups}_name (8), so the longest names that fit 63 bytes
    // are 18, 49, and 55 characters.
    [Theory]
    [InlineData(18, 1, 1, true)]
    [InlineData(19, 1, 1, false)]
    [InlineData(1, 49, 1, true)]
    [InlineData(1, 50, 1, false)]
    [InlineData(1, 1, 55, true)]
    [InlineData(1, 1, 56, false)]
    public void should_refuse_a_table_name_whose_derived_postgresql_names_exceed_63_bytes(
        int grantsLength,
        int definitionsLength,
        int groupsLength,
        bool accepted
    )
    {
        // given
        var services = _ServicesWithTableNames(
            new string('g', grantsLength),
            new string('d', definitionsLength),
            new string('p', groupsLength)
        );
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PermissionsStorageOptions>>().Value;

        // then
        if (accepted)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<OptionsValidationException>().WithMessage("*PostgreSQL truncates identifiers*");
        }
    }

    private static ServiceCollection _ServicesWithTableNames(string grants, string definitions, string groups)
    {
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.PermissionGrantsTableName = grants;
                options.PermissionDefinitionsTableName = definitions;
                options.PermissionGroupDefinitionsTableName = groups;
            });
            setup.UseEntityFramework<OptionsTestDbContext>();
        });

        return services;
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
            modelBuilder.AddHeadlessPermissions(storageOptions, StorageNamingStyle.PascalCase);
        }
    }
}
