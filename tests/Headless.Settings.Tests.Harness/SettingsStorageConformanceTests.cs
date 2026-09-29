// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.Caching;
using Headless.Hosting.Initialization;
using Headless.Settings.Definitions;
using Headless.Settings.Entities;
using Headless.Settings.Models;
using Headless.Settings.Repositories;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Storage behavior every raw-ADO settings provider must share: schema initialization, value round-trip, atomic
/// value batches, and concurrent first writes of one name. Backend-specific behavior (index repair, legacy column
/// renames, NULL provider-key uniqueness, chunked deletes) stays in each provider's integration project.
/// </summary>
public abstract class SettingsStorageConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ISettingsStorageFixture
{
    private const string _Schema = "settings_conformance";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_setting_value()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);

        // when
        await host.StartAsync(AbortToken);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is IHostedLifecycleService);
        var repository = host.Services.GetRequiredService<ISettingValueRecordRepository>();
        var record = new SettingValueRecord(Guid.NewGuid(), "Theme", "Dark", "Global");
        await repository.InsertAsync(record, AbortToken);
        var stored = await repository.FindAsync("Theme", "Global", null, AbortToken);
        var changed = new SettingValueRecord(record.Id, "Theme", "Light", "Global");
        await repository.UpdateAsync(changed, AbortToken);
        var updated = await repository.FindAsync("Theme", "Global", null, AbortToken);

        // then
        initializer.IsInitialized.Should().BeTrue();
        (await fixture.TableExistsAsync(_Schema, "SettingValues", AbortToken)).Should().BeTrue();
        (await fixture.TableExistsAsync(_Schema, "SettingDefinitions", AbortToken)).Should().BeTrue();
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
            Options.Create(new SettingManagementOptions())
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
            Options.Create(new SettingManagementOptions())
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
}
