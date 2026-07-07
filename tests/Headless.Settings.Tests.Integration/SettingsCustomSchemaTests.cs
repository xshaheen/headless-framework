// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Settings;
using Headless.Settings.Definitions;
using Headless.Settings.Entities;
using Headless.Settings.Models;
using Headless.Settings.ValueProviders;
using Headless.Settings.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Tests.TestSetup;

namespace Tests;

public sealed class SettingsCustomSchemaTests(SettingsTestFixture fixture) : SettingsTestBase(fixture)
{
    private const string _Schema = "myapp_settings";
    private const string _ValuesTableName = "tbl_setting_values";
    private const string _DefinitionsTableName = "tbl_setting_definitions";
    private const string _SettingName = "CustomSchemaSetting";

    protected override void ConfigureSettingsStorage(SettingsStorageOptions options)
    {
        options.Schema = _Schema;
        options.SettingValuesTableName = _ValuesTableName;
        options.SettingDefinitionsTableName = _DefinitionsTableName;
    }

    [Fact]
    public async Task should_create_tables_in_custom_schema_with_custom_names()
    {
        // given
        using var host = await _CreateHostWithCustomTablesAsync();

        // when
        var valuesTableExists = await _TableExistsAsync(_Schema, _ValuesTableName);
        var definitionsTableExists = await _TableExistsAsync(_Schema, _DefinitionsTableName);
        var defaultValuesTableExists = await _TableExistsAsync("settings", _ValuesTableName);

        // then
        valuesTableExists.Should().BeTrue();
        definitionsTableExists.Should().BeTrue();
        defaultValuesTableExists.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_default_schema_and_table_names_without_storage_configuration()
    {
        // given — a context that does NOT override ConfigureSettingsStorage, so the storage
        // options keep their defaults. Asserting the EF model mapping exercises the no-configuration
        // path directly, without mutating the shared fixture schema.
        await using var db = _CreateDefaultSchemaContext();

        // when
        var valuesEntity = db.Model.FindEntityType(typeof(SettingValueRecord));
        var definitionsEntity = db.Model.FindEntityType(typeof(SettingDefinitionRecord));

        // then
        valuesEntity.Should().NotBeNull();
        valuesEntity!.GetSchema().Should().Be("settings");
        valuesEntity.GetTableName().Should().Be("SettingValues");
        definitionsEntity.Should().NotBeNull();
        definitionsEntity!.GetSchema().Should().Be("settings");
        definitionsEntity.GetTableName().Should().Be("SettingDefinitions");
    }

    [Fact]
    public async Task should_round_trip_setting_value_under_custom_schema()
    {
        // given
        using var host = await _CreateHostWithCustomTablesAsync(b =>
            b.Services.AddSettingDefinitionProvider<SettingsDefinitionProvider>()
        );
        await using var scope = host.Services.CreateAsyncScope();
        var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        var providerKey = Guid.NewGuid().ToString();
        const string value = "Enabled";

        // when
        await settingManager.SetForUserAsync(providerKey, _SettingName, value, cancellationToken: AbortToken);
        var storedValue = await settingManager.GetForUserAsync(
            providerKey,
            _SettingName,
            cancellationToken: AbortToken
        );

        // then
        storedValue.Should().Be(value);
        (await _TableHasRowsAsync(_Schema, _ValuesTableName)).Should().BeTrue();
        (await _TableHasRowsAsync("settings", "SettingValues")).Should().BeFalse();
    }

    [Fact]
    public async Task should_apply_custom_storage_options_in_shared_dbcontext_without_constructor_injection()
    {
        // given
        var services = new ServiceCollection();
        // AddHeadlessSettings auto-registers the management core, whose _AddCore guards on
        // IStringEncryptionService — register it so the bare provider build does not throw.
        services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "TestPassPhrase123456";
            options.DefaultSalt = "TestSalt"u8.ToArray();
        });
        services.AddDbContextFactory<SharedSettingsContext>(options => options.UseNpgsql(Fixture.SqlConnectionString));
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureStorage(ConfigureSettingsStorage);
            setup.UseEntityFramework<SharedSettingsContext>();
        });
        await using var provider = services.BuildServiceProvider();
        await using var db = await provider
            .GetRequiredService<IDbContextFactory<SharedSettingsContext>>()
            .CreateDbContextAsync(AbortToken);

        // when
        var valuesEntity = db.Model.FindEntityType(typeof(SettingValueRecord));
        var definitionsEntity = db.Model.FindEntityType(typeof(SettingDefinitionRecord));

        // then
        valuesEntity.Should().NotBeNull();
        valuesEntity!.GetSchema().Should().Be(_Schema);
        valuesEntity.GetTableName().Should().Be(_ValuesTableName);
        definitionsEntity.Should().NotBeNull();
        definitionsEntity!.GetSchema().Should().Be(_Schema);
        definitionsEntity.GetTableName().Should().Be(_DefinitionsTableName);
    }

    private async Task<IHost> _CreateHostWithCustomTablesAsync(Action<IHostApplicationBuilder>? configure = null)
    {
        await Fixture.ResetAsync();
        using var setupHost = CreateHost(configure);
        await _RecreateCustomTablesAsync(setupHost.Services);

        return CreateHost(configure);
    }

    private async Task _RecreateCustomTablesAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SettingsTestDbContext>>();
        await using var db = await factory.CreateDbContextAsync(AbortToken);

        await db.Database.ExecuteSqlRawAsync($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE""", AbortToken);
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{_Schema}\"", AbortToken);

        var creator = db.GetService<IRelationalDatabaseCreator>();
        await creator.CreateTablesAsync(AbortToken);
    }

    private async Task<bool> _TableExistsAsync(string schema, string tableName)
    {
        await using var connection = new NpgsqlConnection(Fixture.SqlConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            )
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(schema), schema);
        command.Parameters.AddWithValue("table", tableName);

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<bool> _TableHasRowsAsync(string schema, string tableName)
    {
        await using var connection = new NpgsqlConnection(Fixture.SqlConnectionString);
        await connection.OpenAsync(AbortToken);

        await using var command = new NpgsqlCommand(
            $"""SELECT EXISTS (SELECT 1 FROM "{schema}"."{tableName}")""",
            connection
        );

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private DefaultSchemaSettingsContext _CreateDefaultSchemaContext()
    {
        // No ConfigureStorage call → SettingsStorageOptions stays at its defaults
        // (schema "settings" + default table names).
        var options = new DbContextOptionsBuilder<DefaultSchemaSettingsContext>()
            .UseNpgsql(Fixture.SqlConnectionString)
            .Options;

        return new DefaultSchemaSettingsContext(options, Options.Create(new SettingsStorageOptions()));
    }

    private sealed class DefaultSchemaSettingsContext(
        DbContextOptions<DefaultSchemaSettingsContext> options,
        IOptions<SettingsStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessSettings(storageOptions.Value);
        }
    }

    private sealed class SharedSettingsContext(
        DbContextOptions<SharedSettingsContext> options,
        IOptions<SettingsStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessSettings(storageOptions.Value);
        }
    }

    [UsedImplicitly]
    private sealed class SettingsDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            var definition = context.Add(_SettingName, "Disabled");
            definition.Providers.Add(UserSettingValueProvider.ProviderName);
        }
    }
}
