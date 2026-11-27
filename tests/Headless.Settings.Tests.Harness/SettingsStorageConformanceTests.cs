// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless;
using Headless.Caching;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Primitives;
using Headless.Serializer;
using Headless.Settings;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Storage behavior every raw-ADO settings provider must share: schema initialization, value round-trip, atomic
/// value batches, and concurrent first writes of one name. Backend-specific behavior (index repair, NULL provider-key
/// uniqueness, chunked deletes) stays in each provider's integration project.
/// </summary>
public abstract class SettingsStorageConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ISettingsStorageFixture
{
    private const string _Schema = "settings_conformance";

    [Fact]
    public async Task should_create_the_same_tables_columns_and_indexes_the_ef_model_maps()
    {
        // given — the raw schema contribution and the EF mapping must agree name for name, or an application that
        // provisions with one and reads with the other fails at its first query
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var builder = new DbContextOptionsBuilder<ParityDbContext>();
        fixture.UseEntityFrameworkProvider(builder, fixture.ConnectionString);
        await using var context = new ParityDbContext(
            builder.Options,
            new SettingsStorageOptions { Schema = _Schema },
            fixture.NamingStyle
        );

        // when
        var created = await fixture.ReadStoreObjectsAsync(_Schema, AbortToken);
        var mapped = _MappedObjects(context.Model);

        // then
        created.Columns.Should().BeEquivalentTo(mapped.Columns);
        created.Indexes.Should().BeEquivalentTo(mapped.Indexes);
    }

    private static SettingsStoreObjects _MappedObjects(IModel model)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in model.GetEntityTypes())
        {
            var tableName = entity.GetTableName()!;
            var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                columns.Add($"{tableName}.{property.GetColumnName(table)}");
            }

            indexes.Add($"{tableName}.{entity.FindPrimaryKey()!.GetName()}");

            foreach (var index in entity.GetIndexes())
            {
                indexes.Add($"{tableName}.{index.GetDatabaseName()}");
            }
        }

        return new SettingsStoreObjects(columns, indexes);
    }

    private sealed class ParityDbContext(
        DbContextOptions<ParityDbContext> options,
        SettingsStorageOptions storageOptions,
        StorageNamingStyle style
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessSettings(storageOptions, style);
        }
    }

    [Fact]
    public async Task should_initialize_tables_and_round_trip_setting_value()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);

        // when
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var record = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Global");
        await repository.InsertAsync(record, AbortToken);
        var stored = await repository.FindAsync("Theme", "Global", null, AbortToken);
        var changed = new SettingValueRecord(record.Id, "Theme", "Light", "Global");
        await repository.UpdateAsync(changed, AbortToken);
        var updated = await repository.FindAsync("Theme", "Global", null, AbortToken);

        // then
        var names = new SettingsStorageOptions();
        (await fixture.TableExistsAsync(_Schema, names.ResolveSettingValuesTableName(fixture.NamingStyle), AbortToken))
            .Should()
            .BeTrue();
        (
            await fixture.TableExistsAsync(
                _Schema,
                names.ResolveSettingDefinitionsTableName(fixture.NamingStyle),
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        stored.Should().NotBeNull();
        stored!.Value.Should().Be("Dark");
        stored.CreatedAt.Should().NotBe(default);
        stored.UpdatedAt.Should().BeNull();
        updated.Should().NotBeNull();
        updated!.Value.Should().Be("Light");
        updated.CreatedAt.Should().NotBe(default);
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task should_round_trip_definition_extra_properties_with_their_keys_and_values_as_written()
    {
        // given — keys that a camelCasing dictionary policy would rename, and values the store reads back as CLR
        // primitives
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingDefinitionRecordRepository>();
        var record = new SettingDefinitionRecord(
            Guid.NewGuid(),
            "Billing.Currency",
            "Currency",
            description: null,
            defaultValue: null,
            providers: null,
            isVisibleToClients: false,
            isInherited: true,
            isEncrypted: false
        );
        record.SetProperty("Group", "Billing").SetProperty("Order", 5L).SetProperty("USD", true);

        // when
        await repository.SaveAsync([record], [], [], AbortToken);
        var stored = (await repository.GetListAsync(AbortToken)).Single();

        // then — the definition reads back equal, so a re-seed finds nothing to update
        stored.ExtraProperties.Should().BeEquivalentTo(record.ExtraProperties);
        stored.HasSameData(record).Should().BeTrue();
    }

    [Fact]
    public void should_leave_the_shared_json_serializer_to_the_host()
    {
        // given — the shared serializer carries the host's JSON options for caches and blob metadata; a storage that
        // registered its own first would decide those options by registration order
        using var host = fixture.CreateHost(_Schema);

        // when
        var serializer = host.Services.GetService<IJsonSerializer>();

        // then
        serializer.Should().BeNull();
    }

    [Fact]
    public async Task should_save_a_value_batch_of_inserts_updates_and_deletes()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var kept = new SettingValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");
        var removed = new SettingValueRecord(Guid.NewGuid(), "Font", "old", "Tenant", "t1");
        await repository.InsertAsync(kept, AbortToken);
        await repository.InsertAsync(removed, AbortToken);
        var added = new SettingValueRecord(Guid.NewGuid(), "Locale", "new", "Tenant", "t1");
        var changed = new SettingValueRecord(kept.Id, "Theme", "new", "Tenant", "t1");

        // when
        await repository.SaveAsync([added], [changed], [removed], AbortToken);

        // then
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "new"), ("Locale", "new")]);
    }

    [Fact]
    public async Task should_leave_every_value_unchanged_when_a_write_in_the_batch_fails()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var first = new SettingValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");
        var second = new SettingValueRecord(Guid.NewGuid(), "Font", "old", "Tenant", "t1");
        await repository.InsertAsync(first, AbortToken);
        await repository.InsertAsync(second, AbortToken);
        var added = new SettingValueRecord(Guid.NewGuid(), "Locale", "new", "Tenant", "t1");
        var validUpdate = new SettingValueRecord(first.Id, "Theme", "new", "Tenant", "t1");

        // the column rejects this value, so the batch fails after the insert and the first update already ran
        var failingUpdate = new SettingValueRecord(
            second.Id,
            "Font",
            new string('x', SettingValueRecordConstants.ValueMaxLength + 1),
            "Tenant",
            "t1"
        );

        // when
        var act = async () => await repository.SaveAsync([added], [validUpdate, failingUpdate], [], AbortToken);

        // then
        (await act.Should().ThrowAsync<DbException>())
            .Which.Should()
            .BeAssignableTo(fixture.ProviderExceptionType);
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "old"), ("Font", "old")]);
    }

    [Fact]
    public async Task should_roll_back_the_batch_when_an_updated_row_was_deleted_by_another_writer()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var added = new SettingValueRecord(Guid.NewGuid(), "Theme", "new", "Tenant", "t1");
        var vanished = new SettingValueRecord(Guid.NewGuid(), "Font", "new", "Tenant", "t1");

        // when the batch updates a row nobody stored (another writer deleted it after it was read)
        var act = async () => await repository.SaveAsync([added], [vanished], [], AbortToken);

        // then the whole batch fails and the insert that ran before it is rolled back
        await act.Should().ThrowAsync<DBConcurrencyException>();
        (await repository.GetListAsync("Tenant", "t1", AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_let_every_concurrent_writer_of_a_new_name_succeed()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        // The store reads definitions only to warm its cache; the storage-only host does not register the
        // definition manager's dependencies, so the store is built over the host's real repository and cache.
        var store = new SettingValueStore(
            host.Services.GetRequiredService<ISettingValueRecordRepository>(),
            Substitute.For<ISettingDefinitionManager>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            host.Services.GetRequiredService<ICache<SettingValueCacheItem>>(),
            Options.Create(new SettingManagementOptions()),
            NullLogger<SettingValueStore>.Instance
        );
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();

        // when several writers set the same, not yet stored, name at once
        var writes = Enumerable
            .Range(0, 8)
            .Select(i =>
                store.SetAllAsync(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["Theme"] = $"v{i}" },
                    "Tenant",
                    "t1",
                    AbortToken
                )
            );
        await Task.WhenAll(writes);

        // then every write succeeds and exactly one row holds one of their values
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Should().ContainSingle().Which.Value.Should().MatchRegex("^v[0-7]$");
    }

    [Fact]
    public async Task should_refuse_padded_raw_scope_keys_before_writing_any_enlisted_rows()
    {
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var stored = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Tenant", "acme");
        await repository.InsertAsync(stored, AbortToken);
        await using var connection = fixture.CreateConnection(fixture.ConnectionString);
        await using var unit = await fixture.BeginUnitOfWorkAsync(
            host.Services.GetRequiredService<IUnitOfWorkFactory>(),
            connection,
            AbortToken
        );
        var batchRow = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Tenant", "second");
        var padded = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Tenant", "acme ");

        var find = () => repository.FindAsync("Theme", "Tenant", "acme ", AbortToken);
        var readEnlisted = async () => await repository.GetListEnlistedAsync(unit, "Tenant", "acme ", AbortToken);
        var save = async () => await repository.SaveEnlistedAsync(unit, [batchRow, padded], [], [], AbortToken);
        var saveVersioned = () =>
            repository.SaveIfVersionAsync(["Theme"], "Tenant", "acme ", "unused", [], [], [], AbortToken);

        await find.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("providerKey");
        await readEnlisted.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("providerKey");
        await save.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("providerKey");
        await saveVersioned.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("providerKey");
        await unit.CompleteAsync(AbortToken);

        var found = await repository.FindAsync("Theme", "Tenant", "acme", AbortToken);
        found.Should().NotBeNull();
        found!.Id.Should().Be(stored.Id);
        (await repository.FindAsync("Theme", "Tenant", "second", AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_refuse_a_padded_provider_key_and_leave_the_unpadded_row_intact()
    {
        // given a stored "acme" row; SQL Server compares "acme " equal to it, PostgreSQL does not
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var store = new SettingValueStore(
            repository,
            Substitute.For<ISettingDefinitionManager>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            host.Services.GetRequiredService<ICache<SettingValueCacheItem>>(),
            Options.Create(new SettingManagementOptions()),
            NullLogger<SettingValueStore>.Instance
        );
        await repository.InsertAsync(
            new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Tenant", "acme"),
            AbortToken
        );

        // when
        var set = async () => await store.SetAsync("Theme", "Light", "Tenant", "acme ", AbortToken);
        var get = async () => await store.GetOrDefaultAsync("Theme", "Tenant", "acme ", AbortToken);
        var delete = async () => await store.DeleteAsync("Theme", "Tenant", "acme ", AbortToken);

        // then
        await set.Should().ThrowExactlyAsync<ArgumentException>();
        await get.Should().ThrowExactlyAsync<ArgumentException>();
        await delete.Should().ThrowExactlyAsync<ArgumentException>();
        var stored = await repository.GetListAsync("Tenant", "acme", AbortToken);
        stored.Should().ContainSingle().Which.Value.Should().Be("Dark");
    }

    [Fact]
    public async Task should_not_match_a_stored_prefix_when_a_lookup_value_is_longer_than_its_column()
    {
        // given a row whose name and provider key fill their columns
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var name = new string('n', SettingValueRecordConstants.NameMaxLength);
        var providerKey = new string('k', SettingValueRecordConstants.ProviderKeyMaxLength);
        await repository.InsertAsync(
            new SettingValueRecord(Guid.NewGuid(), name, "v", "Tenant", providerKey),
            AbortToken
        );

        // when each lookup carries one character more than the column holds
        var found = await repository.FindAsync(name + "x", "Tenant", providerKey, AbortToken);
        var all = await repository.FindAllAsync(name + "x", providerName: null, providerKey: null, AbortToken);
        var byNames = await repository.GetListAsync(
            new HashSet<string>(StringComparer.Ordinal) { name + "x" },
            "Tenant",
            providerKey,
            AbortToken
        );
        var byScope = await repository.GetListAsync("Tenant", providerKey + "x", AbortToken);

        // then none of them matches the stored row, which is the lookup value's prefix
        found.Should().BeNull();
        all.Should().BeEmpty();
        byNames.Should().BeEmpty();
        byScope.Should().BeEmpty();
    }

    private const string _LengthsSchema = "settings_lengths";

    // The shape a consumer app composes from two prefixed identifiers: 72 characters, past the default 64.
    private static readonly string _LongProviderKey = "ws_" + new string('a', 32) + ":usr_" + new string('b', 32);

    [Fact]
    public async Task should_create_every_column_at_its_configured_length()
    {
        // given — a distinct length per option, so a column wired to the wrong option shows
        await fixture.DropSchemaAsync(_LengthsSchema, AbortToken);
        using var host = fixture.CreateHost(
            _LengthsSchema,
            configureServices: services =>
                services.Configure<SettingsStorageOptions>(options =>
                {
                    options.SettingValuesNameMaxLength = 201;
                    options.SettingValuesValueMaxLength = 202;
                    options.SettingValuesProviderNameMaxLength = 203;
                    options.SettingValuesProviderKeyMaxLength = 204;
                    options.SettingDefinitionsNameMaxLength = 205;
                    options.SettingDefinitionsDisplayNameMaxLength = 206;
                    options.SettingDefinitionsDescriptionMaxLength = 207;
                    options.SettingDefinitionsDefaultValueMaxLength = 208;
                    options.SettingDefinitionsProvidersMaxLength = 209;
                })
        );

        // when
        await host.StartAsync(AbortToken);
        var lengths = await _ReadColumnLengthsAsync(_LengthsSchema);

        // then
        lengths
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["settingvalues.name"] = 201,
                    ["settingvalues.value"] = 202,
                    ["settingvalues.providername"] = 203,
                    ["settingvalues.providerkey"] = 204,
                    ["settingdefinitions.name"] = 205,
                    ["settingdefinitions.displayname"] = 206,
                    ["settingdefinitions.description"] = 207,
                    ["settingdefinitions.defaultvalue"] = 208,
                    ["settingdefinitions.providers"] = 209,
                }
            );
    }

    [Fact]
    public async Task should_round_trip_a_72_character_provider_key_when_its_column_is_widened_to_128()
    {
        // given
        await fixture.DropSchemaAsync(_LengthsSchema, AbortToken);
        using var host = fixture.CreateHost(
            _LengthsSchema,
            configureServices: services =>
                services.Configure<SettingsStorageOptions>(options => options.SettingValuesProviderKeyMaxLength = 128)
        );
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var record = new SettingValueRecord(Guid.NewGuid(), "Name", "v", "Tenant", _LongProviderKey);

        // when
        await repository.InsertAsync(record, AbortToken);
        var found = await repository.FindAsync("Name", "Tenant", _LongProviderKey, AbortToken);
        var listed = await repository.GetListAsync("Tenant", _LongProviderKey, AbortToken);
        await repository.DeleteAsync([record], AbortToken);
        var afterDelete = await repository.FindAsync("Name", "Tenant", _LongProviderKey, AbortToken);

        // then
        _LongProviderKey.Should().HaveLength(72);
        found.Should().NotBeNull();
        found!.ProviderKey.Should().Be(_LongProviderKey);
        listed.Should().ContainSingle().Which.Id.Should().Be(record.Id);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task should_reject_a_72_character_provider_key_at_the_default_length()
    {
        // given — the default stays 64, so an application that configures nothing keeps the schema it had
        await fixture.DropSchemaAsync(_LengthsSchema, AbortToken);
        using var host = fixture.CreateHost(_LengthsSchema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var record = new SettingValueRecord(Guid.NewGuid(), "Name", "v", "Tenant", _LongProviderKey);

        // when
        var act = () => repository.InsertAsync(record, AbortToken);

        // then
        (await act.Should().ThrowAsync<Exception>())
            .Which.Should()
            .BeAssignableTo(fixture.ProviderExceptionType);
    }

    [Fact]
    public async Task should_refuse_a_versioned_save_after_another_save_of_the_group()
    {
        // given a group read with its version, then changed by another writer
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host);
        await store.SetAllAsync(_Values(("Theme", "Dark"), ("Font", "Serif")), "Tenant", "t1", AbortToken);
        var read = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);
        await store.SetAllAsync(_Values(("Theme", "Blue")), "Tenant", "t1", AbortToken);

        // when
        var act = () =>
            store.SetAllVersionedAsync(
                _Values(("Theme", "Light"), ("Font", "Sans")),
                "Tenant",
                "t1",
                read.Version,
                AbortToken
            );

        // then
        await act.Should().ThrowExactlyAsync<DBConcurrencyException>();
        var stored = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);
        stored.Values.Should().BeEquivalentTo([new SettingValue("Theme", "Blue"), new SettingValue("Font", "Serif")]);
        stored.Version.Should().NotBe(read.Version);
    }

    [Fact]
    public async Task should_refuse_a_versioned_save_after_another_writer_stored_an_absent_name()
    {
        // given a group with nothing stored, then one of its names stored by someone else
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host);
        var read = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);
        await store.SetAllAsync(_Values(("Font", "Serif")), "Tenant", "t1", AbortToken);

        // when the stale form saves the other name and leaves this one empty
        var act = () =>
            store.SetAllVersionedAsync(
                _Values(("Theme", "Dark"), ("Font", null)),
                "Tenant",
                "t1",
                read.Version,
                AbortToken
            );

        // then
        await act.Should().ThrowExactlyAsync<DBConcurrencyException>();
        var stored = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);
        stored.Values.Should().BeEquivalentTo([new SettingValue("Theme", null), new SettingValue("Font", "Serif")]);
    }

    [Fact]
    public async Task should_save_a_current_versioned_group_and_give_it_a_new_version()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host);
        await store.SetAllAsync(_Values(("Theme", "Dark"), ("Font", "Serif")), "Tenant", "t1", AbortToken);
        var read = await store.GetAllVersionedAsync(["Theme", "Font", "Locale"], "Tenant", "t1", AbortToken);

        // when the form changes one value, clears one, and adds one
        var committedVersion = await store.SetAllVersionedAsync(
            _Values(("Theme", "Light"), ("Font", null), ("Locale", "ar")),
            "Tenant",
            "t1",
            read.Version,
            AbortToken
        );
        var replay = () =>
            store.SetAllVersionedAsync(_Values(("Theme", "Dark")), "Tenant", "t1", read.Version, AbortToken);

        // then
        var stored = await store.GetAllVersionedAsync(["Theme", "Font", "Locale"], "Tenant", "t1", AbortToken);
        stored
            .Values.Should()
            .BeEquivalentTo([
                new SettingValue("Theme", "Light"),
                new SettingValue("Font", null),
                new SettingValue("Locale", "ar"),
            ]);
        stored.Version.Should().NotBe(read.Version);
        committedVersion.Should().Be(stored.Version);
        await replay.Should().ThrowExactlyAsync<DBConcurrencyException>();
    }

    [Fact]
    public async Task should_give_each_scope_and_name_set_its_own_version_when_nothing_is_stored()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host);

        // when
        var tenant1 = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);
        var tenant1Again = await store.GetAllVersionedAsync(["Font", "Theme"], "Tenant", "t1", AbortToken);
        var tenant2 = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t2", AbortToken);
        var subset = await store.GetAllVersionedAsync(["Theme"], "Tenant", "t1", AbortToken);
        var subsetSave = () =>
            store.SetAllVersionedAsync(_Values(("Theme", "Dark")), "Tenant", "t1", tenant1.Version, AbortToken);

        // then a version read for one scope or name set never authorizes a save of another
        tenant1Again.Version.Should().Be(tenant1.Version);
        new[] { tenant1.Version, tenant2.Version, subset.Version }.Should().OnlyHaveUniqueItems();
        await subsetSave.Should().ThrowExactlyAsync<DBConcurrencyException>();
    }

    [Fact]
    public async Task should_let_exactly_one_of_two_saves_from_the_same_version_succeed()
    {
        // given two forms read the same empty group; each sets a different name and leaves the other empty, a pair
        // of writes that touch no common row
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host);
        var read = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);

        // when both save at once
        var results = await Task.WhenAll(
            _TrySaveAsync(store, _Values(("Theme", "Dark"), ("Font", null)), read.Version),
            _TrySaveAsync(store, _Values(("Theme", null), ("Font", "Serif")), read.Version)
        );

        // then
        results.Should().ContainSingle(saved => saved);
        var stored = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);
        stored.Values.Count(x => x.Value is not null).Should().Be(1);
    }

    [Fact]
    public async Task should_write_nothing_when_the_repository_sees_a_stale_version()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var existing = new SettingValueRecord(Guid.NewGuid(), "Font", "Serif", "Tenant", "t1");
        await repository.InsertAsync(existing, AbortToken);
        var staleVersion = SettingValueVersion.Compute("Tenant", "t1", ["Theme", "Font"], []);
        var added = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Tenant", "t1");

        // when
        var act = () =>
            repository.SaveIfVersionAsync(
                ["Theme", "Font"],
                "Tenant",
                "t1",
                staleVersion,
                [added],
                [],
                [existing],
                AbortToken
            );

        // then
        await act.Should().ThrowExactlyAsync<DBConcurrencyException>();
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Should().ContainSingle().Which.Should().BeEquivalentTo(existing);
    }

    [Fact]
    public async Task should_preserve_an_unchanged_version_and_accept_an_absent_set_cleared_version()
    {
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host);
        var absent = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);

        var added = await store.SetAllVersionedAsync(
            _Values(("Theme", "Dark"), ("Font", null)),
            "Tenant",
            "t1",
            absent.Version,
            AbortToken
        );
        var unchanged = await store.SetAllVersionedAsync(
            _Values(("Theme", "Dark"), ("Font", null)),
            "Tenant",
            "t1",
            added,
            AbortToken
        );
        unchanged.Should().Be(added);
        var cleared = await store.SetAllVersionedAsync(
            _Values(("Theme", null), ("Font", null)),
            "Tenant",
            "t1",
            unchanged,
            AbortToken
        );
        cleared.Should().Be(absent.Version);

        var restored = await store.SetAllVersionedAsync(
            _Values(("Theme", "Dark"), ("Font", null)),
            "Tenant",
            "t1",
            absent.Version,
            AbortToken
        );
        var current = await store.GetAllVersionedAsync(["Theme", "Font"], "Tenant", "t1", AbortToken);
        restored.Should().Be(current.Version);
    }

    private async Task<bool> _TrySaveAsync(SettingValueStore store, Dictionary<string, string?> values, string version)
    {
        try
        {
            await store.SetAllVersionedAsync(values, "Tenant", "t1", version, AbortToken);

            return true;
        }
        catch (DBConcurrencyException)
        {
            return false;
        }
    }

    private static SettingValueStore _CreateStore(IHost host)
    {
        // The storage-only host does not register the definition manager's dependencies, and versioned reads and
        // writes never consult definitions.
        return new SettingValueStore(
            host.Services.GetRequiredService<ISettingValueRecordRepository>(),
            Substitute.For<ISettingDefinitionManager>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            host.Services.GetRequiredService<ICache<SettingValueCacheItem>>(),
            Options.Create(new SettingManagementOptions()),
            NullLogger<SettingValueStore>.Instance
        );
    }

    private static Dictionary<string, string?> _Values(params (string Name, string? Value)[] values)
    {
        return values.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
    }

    // INFORMATION_SCHEMA reads the same on both databases. Keys are lower-cased with underscores removed, so one
    // expectation reads on either naming style.
    private async Task<Dictionary<string, int>> _ReadColumnLengthsAsync(string schema)
    {
        await using var connection = fixture.CreateConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TABLE_NAME, COLUMN_NAME, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = '{schema}' AND CHARACTER_MAXIMUM_LENGTH > 0
            """;
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var lengths = new Dictionary<string, int>(StringComparer.Ordinal);

        while (await reader.ReadAsync(AbortToken))
        {
            var table = reader.GetString(0);

            if (!table.StartsWith(SchemaRunner.HistoryTableName, StringComparison.Ordinal))
            {
                lengths[_NormalizeName(table) + "." + _NormalizeName(reader.GetString(1))] = reader.GetInt32(2);
            }
        }

        return lengths;
    }

    private static string _NormalizeName(string name)
    {
        return name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    }
}
