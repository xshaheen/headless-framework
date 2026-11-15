// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Hosting;
using Headless.Permissions;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class PermissionsColumnLengthTests : TestBase
{
    [Fact]
    public void should_default_every_column_length_to_its_record_constant()
    {
        // given — a consumer that configures nothing must get the schema the constants always produced
        var options = new PermissionsStorageOptions();

        // then
        options.PermissionGrantsNameMaxLength.Should().Be(PermissionGrantRecordConstants.NameMaxLength);
        options.PermissionGrantsProviderNameMaxLength.Should().Be(PermissionGrantRecordConstants.ProviderNameMaxLength);
        options.PermissionGrantsTenantIdMaxLength.Should().Be(PermissionGrantRecordConstants.TenantIdMaxLength);
        options.PermissionGrantsProviderKeyMaxLength.Should().Be(PermissionGrantRecordConstants.ProviderKeyMaxLength);
        options.PermissionDefinitionsNameMaxLength.Should().Be(PermissionDefinitionRecordConstants.NameMaxLength);
        options
            .PermissionDefinitionsDisplayNameMaxLength.Should()
            .Be(PermissionDefinitionRecordConstants.DisplayNameMaxLength);
        options
            .PermissionDefinitionsProvidersMaxLength.Should()
            .Be(PermissionDefinitionRecordConstants.ProvidersMaxLength);
        options
            .PermissionGroupDefinitionsNameMaxLength.Should()
            .Be(PermissionGroupDefinitionRecordConstants.NameMaxLength);
        options
            .PermissionGroupDefinitionsDisplayNameMaxLength.Should()
            .Be(PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength);
    }

    [Fact]
    public void should_size_every_ef_column_from_its_configured_length()
    {
        // given — a distinct length per option, so a column wired to the wrong option shows
        var storageOptions = new PermissionsStorageOptions
        {
            PermissionGrantsNameMaxLength = 200,
            PermissionGrantsProviderNameMaxLength = 201,
            PermissionGrantsTenantIdMaxLength = 202,
            PermissionGrantsProviderKeyMaxLength = 203,
            PermissionDefinitionsNameMaxLength = 204,
            PermissionDefinitionsDisplayNameMaxLength = 205,
            PermissionDefinitionsProvidersMaxLength = 206,
            PermissionGroupDefinitionsNameMaxLength = 207,
            PermissionGroupDefinitionsDisplayNameMaxLength = 208,
        };
        using var context = new ConfiguredLengthsDbContext(
            new DbContextOptionsBuilder<ConfiguredLengthsDbContext>().UseSqlite("DataSource=:memory:").Options,
            storageOptions
        );

        // then
        _MaxLength<PermissionGrantRecord>(context, nameof(PermissionGrantRecord.Name)).Should().Be(200);
        _MaxLength<PermissionGrantRecord>(context, nameof(PermissionGrantRecord.ProviderName)).Should().Be(201);
        _MaxLength<PermissionGrantRecord>(context, nameof(PermissionGrantRecord.TenantId)).Should().Be(202);
        _MaxLength<PermissionGrantRecord>(context, nameof(PermissionGrantRecord.ProviderKey)).Should().Be(203);
        _MaxLength<PermissionDefinitionRecord>(context, nameof(PermissionDefinitionRecord.Name)).Should().Be(204);
        _MaxLength<PermissionDefinitionRecord>(context, nameof(PermissionDefinitionRecord.ParentName)).Should().Be(204);
        _MaxLength<PermissionDefinitionRecord>(context, nameof(PermissionDefinitionRecord.DisplayName))
            .Should()
            .Be(205);
        _MaxLength<PermissionDefinitionRecord>(context, nameof(PermissionDefinitionRecord.Providers)).Should().Be(206);
        _MaxLength<PermissionGroupDefinitionRecord>(context, nameof(PermissionGroupDefinitionRecord.Name))
            .Should()
            .Be(207);
        _MaxLength<PermissionDefinitionRecord>(context, nameof(PermissionDefinitionRecord.GroupName)).Should().Be(207);
        _MaxLength<PermissionGroupDefinitionRecord>(context, nameof(PermissionGroupDefinitionRecord.DisplayName))
            .Should()
            .Be(208);
    }

    [Fact]
    public void should_carry_configured_lengths_through_registration_to_the_resolved_options()
    {
        // given
        using var provider = _Provider(options => options.PermissionGrantsProviderKeyMaxLength = 128);

        // when
        var resolved = provider.GetRequiredService<IOptions<PermissionsStorageOptions>>().Value;

        // then
        resolved.PermissionGrantsProviderKeyMaxLength.Should().Be(128);
        resolved.PermissionGrantsNameMaxLength.Should().Be(PermissionGrantRecordConstants.NameMaxLength);
    }

    [Theory]
    [InlineData(nameof(PermissionsStorageOptions.PermissionGrantsNameMaxLength))]
    [InlineData(nameof(PermissionsStorageOptions.PermissionGrantsProviderNameMaxLength))]
    [InlineData(nameof(PermissionsStorageOptions.PermissionGrantsTenantIdMaxLength))]
    [InlineData(nameof(PermissionsStorageOptions.PermissionGrantsProviderKeyMaxLength))]
    [InlineData(nameof(PermissionsStorageOptions.PermissionDefinitionsNameMaxLength))]
    [InlineData(nameof(PermissionsStorageOptions.PermissionDefinitionsDisplayNameMaxLength))]
    [InlineData(nameof(PermissionsStorageOptions.PermissionDefinitionsProvidersMaxLength))]
    [InlineData(nameof(PermissionsStorageOptions.PermissionGroupDefinitionsNameMaxLength))]
    [InlineData(nameof(PermissionsStorageOptions.PermissionGroupDefinitionsDisplayNameMaxLength))]
    public void should_reject_a_column_length_outside_one_to_four_thousand(string option)
    {
        // given — 4000 is the widest sized nvarchar on SQL Server, the narrowest of the supported databases
        var property = typeof(PermissionsStorageOptions).GetProperty(option)!;

        foreach (var (length, accepted) in new[] { (0, false), (1, true), (4000, true), (4001, false) })
        {
            using var provider = _Provider(options => property.SetValue(options, length));

            // when
            var act = () => provider.GetRequiredService<IOptions<PermissionsStorageOptions>>().Value;

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
        // given — a permission name one character past the default column
        var group = new PermissionGroupDefinition("Group", "Group");
        var permission = group.AddChild(new string('p', PermissionDefinitionRecordConstants.NameMaxLength + 1), "P");

        // when
        var refuse = () => _Serializer(new PermissionsStorageOptions()).Serialize(permission, group);
        var record = _Serializer(new PermissionsStorageOptions { PermissionDefinitionsNameMaxLength = 200 })
            .Serialize(permission, group);

        // then
        refuse.Should().Throw<ArgumentOutOfRangeException>();
        record.Name.Should().Be(permission.Name);
    }

    [Fact]
    public void should_refuse_a_group_name_longer_than_its_configured_column()
    {
        // given
        var group = new PermissionGroupDefinition(new string('g', 11), "Group");
        var serializer = _Serializer(new PermissionsStorageOptions { PermissionGroupDefinitionsNameMaxLength = 10 });

        // when
        var act = () => serializer.Serialize(group);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static PermissionDefinitionSerializer _Serializer(PermissionsStorageOptions options)
    {
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        return new PermissionDefinitionSerializer(guidGenerator, Options.Create(options));
    }

    private static ServiceProvider _Provider(Action<PermissionsStorageOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup =>
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
        PermissionsStorageOptions storageOptions
    ) : DbContext(options)
    {
        public DbSet<PermissionGrantRecord> PermissionGrantRecords => Set<PermissionGrantRecord>();

        public DbSet<PermissionDefinitionRecord> PermissionDefinitionRecords => Set<PermissionDefinitionRecord>();

        public DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitionRecords =>
            Set<PermissionGroupDefinitionRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessPermissions(storageOptions, StorageNamingStyle.PascalCase);
        }
    }
}
