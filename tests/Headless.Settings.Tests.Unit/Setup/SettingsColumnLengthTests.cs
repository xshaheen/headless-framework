// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Hosting;
using Headless.Primitives;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class SettingsColumnLengthTests : TestBase
{
    [Fact]
    public void should_default_every_column_length_to_its_record_constant()
    {
        // given — a consumer that configures nothing must get the schema the constants always produced
        var options = new SettingsStorageOptions();

        // then
        options.SettingValuesNameMaxLength.Should().Be(SettingValueRecordConstants.NameMaxLength);
        options.SettingValuesValueMaxLength.Should().Be(SettingValueRecordConstants.ValueMaxLength);
        options.SettingValuesProviderNameMaxLength.Should().Be(SettingValueRecordConstants.ProviderNameMaxLength);
        options.SettingValuesProviderKeyMaxLength.Should().Be(SettingValueRecordConstants.ProviderKeyMaxLength);
        options.SettingDefinitionsNameMaxLength.Should().Be(SettingDefinitionRecordConstants.NameMaxLength);
        options
            .SettingDefinitionsDisplayNameMaxLength.Should()
            .Be(SettingDefinitionRecordConstants.DisplayNameMaxLength);
        options
            .SettingDefinitionsDescriptionMaxLength.Should()
            .Be(SettingDefinitionRecordConstants.DescriptionMaxLength);
        options
            .SettingDefinitionsDefaultValueMaxLength.Should()
            .Be(SettingDefinitionRecordConstants.DefaultValueMaxLength);
        options.SettingDefinitionsProvidersMaxLength.Should().Be(SettingDefinitionRecordConstants.ProvidersMaxLength);
    }

    [Fact]
    public void should_size_every_ef_column_from_its_configured_length()
    {
        // given — a distinct length per option, so a column wired to the wrong option shows
        var storageOptions = new SettingsStorageOptions
        {
            SettingValuesNameMaxLength = 200,
            SettingValuesValueMaxLength = 201,
            SettingValuesProviderNameMaxLength = 202,
            SettingValuesProviderKeyMaxLength = 203,
            SettingDefinitionsNameMaxLength = 204,
            SettingDefinitionsDisplayNameMaxLength = 205,
            SettingDefinitionsDescriptionMaxLength = 206,
            SettingDefinitionsDefaultValueMaxLength = 207,
            SettingDefinitionsProvidersMaxLength = 208,
        };
        using var context = new ConfiguredLengthsDbContext(
            new DbContextOptionsBuilder<ConfiguredLengthsDbContext>().UseSqlite("DataSource=:memory:").Options,
            storageOptions
        );

        // then
        _MaxLength<SettingValueRecord>(context, nameof(SettingValueRecord.Name)).Should().Be(200);
        _MaxLength<SettingValueRecord>(context, nameof(SettingValueRecord.Value)).Should().Be(201);
        _MaxLength<SettingValueRecord>(context, nameof(SettingValueRecord.ProviderName)).Should().Be(202);
        _MaxLength<SettingValueRecord>(context, nameof(SettingValueRecord.ProviderKey)).Should().Be(203);
        _MaxLength<SettingDefinitionRecord>(context, nameof(SettingDefinitionRecord.Name)).Should().Be(204);
        _MaxLength<SettingDefinitionRecord>(context, nameof(SettingDefinitionRecord.DisplayName)).Should().Be(205);
        _MaxLength<SettingDefinitionRecord>(context, nameof(SettingDefinitionRecord.Description)).Should().Be(206);
        _MaxLength<SettingDefinitionRecord>(context, nameof(SettingDefinitionRecord.DefaultValue)).Should().Be(207);
        _MaxLength<SettingDefinitionRecord>(context, nameof(SettingDefinitionRecord.Providers)).Should().Be(208);
    }

    [Fact]
    public void should_carry_configured_lengths_through_registration_to_the_resolved_options()
    {
        // given
        using var provider = _Provider(options => options.SettingValuesProviderKeyMaxLength = 128);

        // when
        var resolved = provider.GetRequiredService<IOptions<SettingsStorageOptions>>().Value;

        // then
        resolved.SettingValuesProviderKeyMaxLength.Should().Be(128);
        resolved.SettingValuesNameMaxLength.Should().Be(SettingValueRecordConstants.NameMaxLength);
    }

    [Theory]
    [InlineData(nameof(SettingsStorageOptions.SettingValuesNameMaxLength))]
    [InlineData(nameof(SettingsStorageOptions.SettingValuesValueMaxLength))]
    [InlineData(nameof(SettingsStorageOptions.SettingValuesProviderNameMaxLength))]
    [InlineData(nameof(SettingsStorageOptions.SettingValuesProviderKeyMaxLength))]
    [InlineData(nameof(SettingsStorageOptions.SettingDefinitionsNameMaxLength))]
    [InlineData(nameof(SettingsStorageOptions.SettingDefinitionsDisplayNameMaxLength))]
    [InlineData(nameof(SettingsStorageOptions.SettingDefinitionsDescriptionMaxLength))]
    [InlineData(nameof(SettingsStorageOptions.SettingDefinitionsDefaultValueMaxLength))]
    [InlineData(nameof(SettingsStorageOptions.SettingDefinitionsProvidersMaxLength))]
    public void should_reject_a_column_length_outside_one_to_four_thousand(string option)
    {
        // given — 4000 is the widest sized nvarchar on SQL Server, the narrowest of the supported databases
        var property = typeof(SettingsStorageOptions).GetProperty(option)!;

        foreach (var (length, accepted) in new[] { (0, false), (1, true), (4000, true), (4001, false) })
        {
            using var provider = _Provider(options => property.SetValue(options, length));

            // when
            var act = () => provider.GetRequiredService<IOptions<SettingsStorageOptions>>().Value;

            // then
            if (accepted)
            {
                act.Should().NotThrow();
            }
            else
            {
                act.Should().Throw<OptionsValidationException>().WithMessage($"*{option}*");
            }
        }
    }

    [Fact]
    public void should_refuse_a_definition_longer_than_its_configured_column_and_accept_it_once_raised()
    {
        // given — a setting name one character past the default column
        var setting = new SettingDefinition(new string('s', SettingDefinitionRecordConstants.NameMaxLength + 1));

        // when
        var refuse = () => _Serializer(new SettingsStorageOptions()).Serialize(setting);
        var record = _Serializer(new SettingsStorageOptions { SettingDefinitionsNameMaxLength = 200 })
            .Serialize(setting);

        // then
        refuse.Should().Throw<ArgumentOutOfRangeException>();
        record.Name.Should().Be(setting.Name);
    }

    [Fact]
    public void should_refuse_a_default_value_longer_than_its_configured_column()
    {
        // given
        var setting = new SettingDefinition("Name", defaultValue: new string('v', 11));
        var serializer = _Serializer(new SettingsStorageOptions { SettingDefinitionsDefaultValueMaxLength = 10 });

        // when
        var act = () => serializer.Serialize(setting);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static SettingDefinitionSerializer _Serializer(SettingsStorageOptions options)
    {
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        return new SettingDefinitionSerializer(guidGenerator, Options.Create(options));
    }

    private static ServiceProvider _Provider(Action<SettingsStorageOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureStorage(configure);
            setup.UseEntityFramework<RegistrationDbContext>();
        });

        return services.BuildServiceProvider();
    }

    private static int? _MaxLength<TEntity>(DbContext context, string property)
    {
        return context.Model.FindEntityType(typeof(TEntity))!.FindProperty(property)!.GetMaxLength();
    }

    private sealed class RegistrationDbContext(DbContextOptions<RegistrationDbContext> options) : DbContext(options);

    // EF caches one model per context type, so the configured lengths need a context type of their own.
    private sealed class ConfiguredLengthsDbContext(
        DbContextOptions<ConfiguredLengthsDbContext> options,
        SettingsStorageOptions storageOptions
    ) : DbContext(options)
    {
        public DbSet<SettingValueRecord> SettingValueRecords => Set<SettingValueRecord>();

        public DbSet<SettingDefinitionRecord> SettingDefinitionRecords => Set<SettingDefinitionRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessSettings(storageOptions, StorageNamingStyle.PascalCase);
        }
    }
}
