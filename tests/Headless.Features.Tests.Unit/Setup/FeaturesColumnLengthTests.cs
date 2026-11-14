// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Features;
using Headless.Hosting;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class FeaturesColumnLengthTests : TestBase
{
    [Fact]
    public void should_default_every_column_length_to_its_record_constant()
    {
        // given — a consumer that configures nothing must get the schema the constants always produced
        var options = new FeaturesStorageOptions();

        // then
        options.FeatureValuesNameMaxLength.Should().Be(FeatureValueRecordConstants.NameMaxLength);
        options.FeatureValuesValueMaxLength.Should().Be(FeatureValueRecordConstants.ValueMaxLength);
        options.FeatureValuesProviderNameMaxLength.Should().Be(FeatureValueRecordConstants.ProviderNameMaxLength);
        options.FeatureValuesProviderKeyMaxLength.Should().Be(FeatureValueRecordConstants.ProviderKeyMaxLength);
        options.FeatureDefinitionsNameMaxLength.Should().Be(FeatureDefinitionRecordConstants.NameMaxLength);
        options
            .FeatureDefinitionsDisplayNameMaxLength.Should()
            .Be(FeatureDefinitionRecordConstants.DisplayNameMaxLength);
        options
            .FeatureDefinitionsDescriptionMaxLength.Should()
            .Be(FeatureDefinitionRecordConstants.DescriptionMaxLength);
        options
            .FeatureDefinitionsDefaultValueMaxLength.Should()
            .Be(FeatureDefinitionRecordConstants.DefaultValueMaxLength);
        options.FeatureDefinitionsProvidersMaxLength.Should().Be(FeatureDefinitionRecordConstants.ProvidersMaxLength);
        options.FeatureGroupDefinitionsNameMaxLength.Should().Be(FeatureGroupDefinitionRecordConstants.NameMaxLength);
        options
            .FeatureGroupDefinitionsDisplayNameMaxLength.Should()
            .Be(FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength);
    }

    [Fact]
    public void should_size_every_ef_column_from_its_configured_length()
    {
        // given — a distinct length per option, so a column wired to the wrong option shows
        var storageOptions = new FeaturesStorageOptions
        {
            FeatureValuesNameMaxLength = 200,
            FeatureValuesValueMaxLength = 201,
            FeatureValuesProviderNameMaxLength = 202,
            FeatureValuesProviderKeyMaxLength = 203,
            FeatureDefinitionsNameMaxLength = 204,
            FeatureDefinitionsDisplayNameMaxLength = 205,
            FeatureDefinitionsDescriptionMaxLength = 206,
            FeatureDefinitionsDefaultValueMaxLength = 207,
            FeatureDefinitionsProvidersMaxLength = 208,
            FeatureGroupDefinitionsNameMaxLength = 209,
            FeatureGroupDefinitionsDisplayNameMaxLength = 210,
        };
        using var context = new ConfiguredLengthsDbContext(
            new DbContextOptionsBuilder<ConfiguredLengthsDbContext>().UseSqlite("DataSource=:memory:").Options,
            storageOptions
        );

        // then
        _MaxLength<FeatureValueRecord>(context, nameof(FeatureValueRecord.Name)).Should().Be(200);
        _MaxLength<FeatureValueRecord>(context, nameof(FeatureValueRecord.Value)).Should().Be(201);
        _MaxLength<FeatureValueRecord>(context, nameof(FeatureValueRecord.ProviderName)).Should().Be(202);
        _MaxLength<FeatureValueRecord>(context, nameof(FeatureValueRecord.ProviderKey)).Should().Be(203);
        _MaxLength<FeatureDefinitionRecord>(context, nameof(FeatureDefinitionRecord.Name)).Should().Be(204);
        _MaxLength<FeatureDefinitionRecord>(context, nameof(FeatureDefinitionRecord.ParentName)).Should().Be(204);
        _MaxLength<FeatureDefinitionRecord>(context, nameof(FeatureDefinitionRecord.DisplayName)).Should().Be(205);
        _MaxLength<FeatureDefinitionRecord>(context, nameof(FeatureDefinitionRecord.Description)).Should().Be(206);
        _MaxLength<FeatureDefinitionRecord>(context, nameof(FeatureDefinitionRecord.DefaultValue)).Should().Be(207);
        _MaxLength<FeatureDefinitionRecord>(context, nameof(FeatureDefinitionRecord.Providers)).Should().Be(208);
        _MaxLength<FeatureGroupDefinitionRecord>(context, nameof(FeatureGroupDefinitionRecord.Name)).Should().Be(209);
        _MaxLength<FeatureDefinitionRecord>(context, nameof(FeatureDefinitionRecord.GroupName)).Should().Be(209);
        _MaxLength<FeatureGroupDefinitionRecord>(context, nameof(FeatureGroupDefinitionRecord.DisplayName))
            .Should()
            .Be(210);
    }

    [Fact]
    public void should_carry_configured_lengths_through_registration_to_the_resolved_options()
    {
        // given
        using var provider = _Provider(options => options.FeatureValuesProviderKeyMaxLength = 128);

        // when
        var resolved = provider.GetRequiredService<IOptions<FeaturesStorageOptions>>().Value;

        // then
        resolved.FeatureValuesProviderKeyMaxLength.Should().Be(128);
        resolved.FeatureValuesNameMaxLength.Should().Be(FeatureValueRecordConstants.NameMaxLength);
    }

    [Theory]
    [InlineData(nameof(FeaturesStorageOptions.FeatureValuesNameMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureValuesValueMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureValuesProviderNameMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureValuesProviderKeyMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureDefinitionsNameMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureDefinitionsDisplayNameMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureDefinitionsDescriptionMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureDefinitionsDefaultValueMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureDefinitionsProvidersMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureGroupDefinitionsNameMaxLength))]
    [InlineData(nameof(FeaturesStorageOptions.FeatureGroupDefinitionsDisplayNameMaxLength))]
    public void should_reject_a_column_length_outside_one_to_four_thousand(string option)
    {
        // given — 4000 is the widest sized nvarchar on SQL Server, the narrowest of the supported databases
        var property = typeof(FeaturesStorageOptions).GetProperty(option)!;

        foreach (var (length, accepted) in new[] { (0, false), (1, true), (4000, true), (4001, false) })
        {
            using var provider = _Provider(options => property.SetValue(options, length));

            // when
            var act = () => provider.GetRequiredService<IOptions<FeaturesStorageOptions>>().Value;

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
        // given — a feature name one character past the default column
        var group = new FeatureGroupDefinition("Group");
        var feature = group.AddChild(
            new FeatureDefinitionCreateOptions
            {
                Name = new string('f', FeatureDefinitionRecordConstants.NameMaxLength + 1),
            }
        );

        // when
        var refuse = () => _Serializer(new FeaturesStorageOptions()).Serialize(feature, group);
        var record = _Serializer(new FeaturesStorageOptions { FeatureDefinitionsNameMaxLength = 200 })
            .Serialize(feature, group);

        // then
        refuse.Should().Throw<ArgumentOutOfRangeException>();
        record.Name.Should().Be(feature.Name);
    }

    [Fact]
    public void should_refuse_a_group_name_longer_than_its_configured_column()
    {
        // given
        var group = new FeatureGroupDefinition(new string('g', 11));
        var serializer = _Serializer(new FeaturesStorageOptions { FeatureGroupDefinitionsNameMaxLength = 10 });

        // when
        var act = () => serializer.Serialize(group);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static FeatureDefinitionSerializer _Serializer(FeaturesStorageOptions options)
    {
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        return new FeatureDefinitionSerializer(guidGenerator, Options.Create(options));
    }

    private static ServiceProvider _Provider(Action<FeaturesStorageOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessFeatures(setup =>
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
        FeaturesStorageOptions storageOptions
    ) : DbContext(options)
    {
        public DbSet<FeatureValueRecord> FeatureValueRecords => Set<FeatureValueRecord>();

        public DbSet<FeatureDefinitionRecord> FeatureDefinitionRecords => Set<FeatureDefinitionRecord>();

        public DbSet<FeatureGroupDefinitionRecord> FeatureGroupDefinitionRecords => Set<FeatureGroupDefinitionRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessFeatures(storageOptions, StorageNamingStyle.PascalCase);
        }
    }
}
