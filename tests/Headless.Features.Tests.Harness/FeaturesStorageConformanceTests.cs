// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.Caching;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Features.Values;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Storage behavior every raw-ADO features provider must share: schema initialization, value and definition
/// round-trip, name-filtered reads, atomic value batches, and concurrent first writes of one name. Backend-specific
/// behavior (index repair, legacy column renames, chunk sizes, NULL provider-key uniqueness, chunked deletes) stays
/// in each provider's integration project.
/// </summary>
public abstract class FeaturesStorageConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IFeaturesStorageFixture
{
    private const string _Schema = "features_conformance";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_feature_value_and_definition()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);

        // when
        await host.StartAsync(AbortToken);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .Single(x => x is IHostedLifecycleService);
        var valueRepository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var definitionRepository = host.Services.GetRequiredService<IFeatureDefinitionRecordRepository>();
        var record = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "true", "Edition", "pro");
        var group = new FeatureGroupDefinitionRecord(Guid.NewGuid(), "Checkout", "Checkout");
        var feature = new FeatureDefinitionRecord(
            Guid.NewGuid(),
            "Checkout",
            "Checkout.Enabled",
            null,
            "Checkout enabled"
        );

        await valueRepository.InsertAsync(record, AbortToken);
        await definitionRepository.SaveAsync([group], [], [], [feature], [], [], AbortToken);
        var stored = await valueRepository.FindAsync("Checkout.Enabled", "Edition", "pro", AbortToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(AbortToken);
        var storedFeatures = await definitionRepository.GetFeaturesListAsync(AbortToken);

        // then
        initializer.IsInitialized.Should().BeTrue();
        (await fixture.TableExistsAsync(_Schema, "FeatureValues", AbortToken)).Should().BeTrue();
        (await fixture.TableExistsAsync(_Schema, "FeatureDefinitions", AbortToken)).Should().BeTrue();
        (await fixture.TableExistsAsync(_Schema, "FeatureGroupDefinitions", AbortToken)).Should().BeTrue();
        stored.Should().NotBeNull();
        stored!.Value.Should().Be("true");
        storedGroups.Should().ContainSingle(x => x.Name == "Checkout");
        storedFeatures.Should().ContainSingle(x => x.Name == "Checkout.Enabled");
    }

    [Fact]
    public async Task should_save_a_value_batch_of_inserts_updates_and_deletes()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var kept = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "old", "Tenant", "t1");
        var removed = new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "old", "Tenant", "t1");
        await repository.InsertAsync(kept, AbortToken);
        await repository.InsertAsync(removed, AbortToken);
        var added = new FeatureValueRecord(Guid.NewGuid(), "Export.Enabled", "new", "Tenant", "t1");
        var changed = new FeatureValueRecord(kept.Id, "Checkout.Enabled", "new", "Tenant", "t1");

        // when
        await repository.SaveAsync([added], [changed], [removed], AbortToken);

        // then
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("Checkout.Enabled", "new"), ("Export.Enabled", "new")]);
    }

    [Fact]
    public async Task should_read_only_the_requested_names_from_a_scope()
    {
        // given three values in one scope and a same-named value in another scope
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "a", "Tenant", "t1"),
            AbortToken
        );
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "b", "Tenant", "t1"),
            AbortToken
        );
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Export.Enabled", "c", "Tenant", "t1"),
            AbortToken
        );
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "other", "Tenant", "t2"),
            AbortToken
        );

        // when
        var stored = await repository.GetListAsync(
            new HashSet<string>(StringComparer.Ordinal) { "Checkout.Enabled", "Export.Enabled" },
            "Tenant",
            "t1",
            AbortToken
        );

        // then
        stored
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("Checkout.Enabled", "a"), ("Export.Enabled", "c")]);
    }

    [Fact]
    public async Task should_leave_every_value_unchanged_when_a_write_in_the_batch_fails()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var first = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "old", "Tenant", "t1");
        var second = new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "old", "Tenant", "t1");
        await repository.InsertAsync(first, AbortToken);
        await repository.InsertAsync(second, AbortToken);
        var added = new FeatureValueRecord(Guid.NewGuid(), "Export.Enabled", "new", "Tenant", "t1");
        var validUpdate = new FeatureValueRecord(first.Id, "Checkout.Enabled", "new", "Tenant", "t1");

        // the column rejects this value, so the batch fails after the insert and the first update already ran
        var failingUpdate = new FeatureValueRecord(
            second.Id,
            "Reports.Enabled",
            new string('x', FeatureValueRecordConstants.ValueMaxLength + 1),
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
        stored
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("Checkout.Enabled", "old"), ("Reports.Enabled", "old")]);
    }

    [Fact]
    public async Task should_roll_back_the_batch_when_an_updated_row_was_deleted_by_another_writer()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var added = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "new", "Tenant", "t1");
        var vanished = new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "new", "Tenant", "t1");

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
        var store = new FeatureValueStore(
            Substitute.For<IFeatureDefinitionManager>(),
            host.Services.GetRequiredService<IFeatureValueRecordRepository>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            host.Services.GetRequiredService<ICache>()
        );
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();

        // when several writers set the same, not yet stored, name at once
        var writes = Enumerable
            .Range(0, 8)
            .Select(i =>
                store.SetAllAsync(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["Checkout.Enabled"] = $"v{i}" },
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
        var repository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var store = new FeatureValueStore(
            Substitute.For<IFeatureDefinitionManager>(),
            repository,
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            host.Services.GetRequiredService<ICache>()
        );
        await repository.InsertAsync(
            new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "true", "Tenant", "acme"),
            AbortToken
        );

        // when
        var set = async () => await store.SetAsync("Checkout.Enabled", "false", "Tenant", "acme ", AbortToken);
        var get = async () => await store.GetOrDefaultAsync("Checkout.Enabled", "Tenant", "acme ", AbortToken);
        var delete = async () => await store.DeleteAsync("Checkout.Enabled", "Tenant", "acme ", AbortToken);

        // then
        await set.Should().ThrowExactlyAsync<ArgumentException>();
        await get.Should().ThrowExactlyAsync<ArgumentException>();
        await delete.Should().ThrowExactlyAsync<ArgumentException>();
        var stored = await repository.GetListAsync("Tenant", "acme", AbortToken);
        stored.Should().ContainSingle().Which.Value.Should().Be("true");
    }
}
