// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Security;
using Headless.Settings;
using Headless.Settings.Entities;
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
            options.DefaultSalt = "TestSalt"u8.ToArray();
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
        resolved.Schema.Should().Be("settings");
        resolved.SettingValuesTableName.Should().Be("SettingValues");
        resolved.SettingDefinitionsTableName.Should().Be("SettingDefinitions");
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
            modelBuilder.AddHeadlessSettings(storageOptions);
        }
    }
}
