// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Security;
using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class SettingsStorageOptionsTests
{
    // AddHeadlessSettings auto-registers the management core, which requires IStringEncryptionService.
    private static ServiceCollection _CreateServicesWithEncryption()
    {
        var services = new ServiceCollection();
        services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "TestPassPhrase123456";
            options.DefaultSalt = [.. "TestSalt"u8];
        });
        return services;
    }

    [Theory]
    [InlineData("", "SettingValues", "SettingDefinitions")]
    [InlineData("settings", "", "SettingDefinitions")]
    [InlineData("settings", "SettingValues", "")]
    [InlineData("   ", "SettingValues", "SettingDefinitions")]
    [InlineData("settings", "   ", "SettingDefinitions")]
    [InlineData("settings", "SettingValues", "   ")]
    public void should_reject_storage_options_when_any_field_is_blank(
        string schema,
        string valuesTable,
        string definitionsTable
    )
    {
        // given
        var services = _CreateServicesWithEncryption();
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = schema;
                options.SettingValuesTableName = valuesTable;
                options.SettingDefinitionsTableName = definitionsTable;
            });
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SettingsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_accept_storage_options_when_all_fields_are_non_blank()
    {
        // given
        var services = _CreateServicesWithEncryption();
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = "custom_settings";
                options.SettingValuesTableName = "tbl_setting_values";
                options.SettingDefinitionsTableName = "tbl_setting_definitions";
            });
            setup.UseEntityFramework<OptionsTestDbContext>();
        });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SettingsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        var resolved = act.Should().NotThrow().Subject;
        resolved.Schema.Should().Be("custom_settings");
        resolved.SettingValuesTableName.Should().Be("tbl_setting_values");
        resolved.SettingDefinitionsTableName.Should().Be("tbl_setting_definitions");
    }

    [Fact]
    public void should_accept_storage_options_when_left_at_defaults()
    {
        // given
        var services = _CreateServicesWithEncryption();
        services.AddHeadlessSettings(setup => setup.UseEntityFramework<OptionsTestDbContext>());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SettingsStorageOptions>>();

        // when
        var act = () => options.Value;

        // then
        var resolved = act.Should().NotThrow().Subject;
        resolved.Schema.Should().Be("headless");
        resolved.SettingValuesTableName.Should().BeNull();
        resolved.SettingDefinitionsTableName.Should().BeNull();
    }

    [Theory]
    [InlineData(StorageNamingStyle.PascalCase, "SettingValues", "SettingDefinitions")]
    [InlineData(StorageNamingStyle.SnakeCase, "setting_values", "setting_definitions")]
    public void should_resolve_default_table_names_in_the_database_naming_style(
        StorageNamingStyle style,
        string valuesTable,
        string definitionsTable
    )
    {
        // given
        var options = new SettingsStorageOptions();

        // when / then
        options.ResolveSettingValuesTableName(style).Should().Be(valuesTable);
        options.ResolveSettingDefinitionsTableName(style).Should().Be(definitionsTable);
    }

    [Fact]
    public void should_resolve_configured_table_names_verbatim_in_every_naming_style()
    {
        // given
        var options = new SettingsStorageOptions
        {
            SettingValuesTableName = "MyValues",
            SettingDefinitionsTableName = "MyDefinitions",
        };

        // when / then
        options.ResolveSettingValuesTableName(StorageNamingStyle.SnakeCase).Should().Be("MyValues");
        options.ResolveSettingDefinitionsTableName(StorageNamingStyle.PascalCase).Should().Be("MyDefinitions");
    }

    [Fact]
    public void should_accept_the_snake_case_defaults_when_configured_explicitly()
    {
        // given — the conventional names themselves must fit, or the defaults' derived names would be truncated
        var services = _ServicesWithTableNames("setting_values", "setting_definitions");
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<SettingsStorageOptions>>().Value;

        // then
        act.Should().NotThrow();
    }

    // The longest derived PostgreSQL names are ix_{values}_name_provider_name_null_provider_key (40 bytes besides the
    // table) and ix_{definitions}_name (8), so the longest names that fit 63 bytes are 23 and 55 characters.
    [Theory]
    [InlineData(23, 1, true)]
    [InlineData(24, 1, false)]
    [InlineData(1, 55, true)]
    [InlineData(1, 56, false)]
    public void should_refuse_a_table_name_whose_derived_postgresql_names_exceed_63_bytes(
        int valuesLength,
        int definitionsLength,
        bool accepted
    )
    {
        // given
        var services = _ServicesWithTableNames(new string('v', valuesLength), new string('d', definitionsLength));
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<SettingsStorageOptions>>().Value;

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

    private static ServiceCollection _ServicesWithTableNames(string values, string definitions)
    {
        var services = _CreateServicesWithEncryption();
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.SettingValuesTableName = values;
                options.SettingDefinitionsTableName = definitions;
            });
            setup.UseEntityFramework<OptionsTestDbContext>();
        });

        return services;
    }

    [Fact]
    public void should_reject_multiple_storage_provider_registrations()
    {
        // given
        var services = _CreateServicesWithEncryption();
        services.AddHeadlessSettings(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        // when
        var action = () => services.AddHeadlessSettings(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*exactly one storage provider*");
    }

    [Fact]
    public void should_apply_setting_model_configuration_when_entities_are_already_discovered()
    {
        // given
        var storageOptions = new SettingsStorageOptions
        {
            Schema = "custom_settings",
            SettingValuesTableName = "custom_setting_values",
            SettingDefinitionsTableName = "custom_setting_definitions",
        };
        using var context = new ExistingSettingsEntityDbContext(
            new DbContextOptionsBuilder<ExistingSettingsEntityDbContext>().UseSqlite("DataSource=:memory:").Options,
            storageOptions
        );

        // when
        var settingValueEntity = context.Model.FindEntityType(typeof(SettingValueRecord));
        var settingDefinitionEntity = context.Model.FindEntityType(typeof(SettingDefinitionRecord));

        // then
        settingValueEntity.Should().NotBeNull();
        settingValueEntity!.GetSchema().Should().Be("custom_settings");
        settingValueEntity.GetTableName().Should().Be("custom_setting_values");
        settingDefinitionEntity.Should().NotBeNull();
        settingDefinitionEntity!.GetTableName().Should().Be("custom_setting_definitions");
    }

    private sealed class OptionsTestDbContext(DbContextOptions<OptionsTestDbContext> options) : DbContext(options);

    private sealed class ExistingSettingsEntityDbContext(
        DbContextOptions<ExistingSettingsEntityDbContext> options,
        SettingsStorageOptions storageOptions
    ) : DbContext(options)
    {
        public DbSet<SettingValueRecord> SettingValues => Set<SettingValueRecord>();

        public DbSet<SettingDefinitionRecord> SettingDefinitions => Set<SettingDefinitionRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessSettings(storageOptions, StorageNamingStyle.PascalCase);
        }
    }
}
