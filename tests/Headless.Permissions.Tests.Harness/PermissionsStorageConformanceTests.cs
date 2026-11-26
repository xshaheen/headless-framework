// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless;
using Headless.Caching;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Primitives;
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
/// Storage behavior every raw-ADO permissions provider must share: schema initialization and parity with the EF
/// model, grant and definition round-trip, batched and atomic grant writes, tenant-scoped reads, key and length
/// guards, and configurable column lengths. Backend-specific behavior (index repair, chunk sizes, parameter sizing,
/// driver error codes) stays in each provider's integration project.
/// </summary>
public abstract class PermissionsStorageConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IPermissionsStorageFixture
{
    private const string _Schema = "permissions_conformance";

    // The shape a consumer app composes from two prefixed identifiers: 72 characters, past the default 64.
    private static readonly string _LongProviderKey = "ws_" + new string('a', 32) + ":usr_" + new string('b', 32);

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
            new PermissionsStorageOptions { Schema = _Schema }
        );

        // when
        var created = await fixture.ReadStoreObjectsAsync(_Schema, AbortToken);
        var (mappedColumns, mappedIndexes) = _MappedObjects(context.Model);

        // then — the schema runner's history table sits beside the feature's tables but is no part of the EF model
        _WithoutHistory(created.Columns).Should().BeEquivalentTo(mappedColumns);
        _WithoutHistory(created.Indexes).Should().BeEquivalentTo(mappedIndexes);
    }

    [Fact]
    public async Task should_initialize_tables_and_round_trip_permission_grant_and_definition()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);

        // when
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var definitionRepository = host.Services.GetRequiredService<IPermissionDefinitionRecordRepository>();
        var record = new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", "Role", "admin", isGranted: true);
        var group = new PermissionGroupDefinitionRecord(Guid.NewGuid(), "Users", "Users");
        var permission = new PermissionDefinitionRecord(Guid.NewGuid(), "Users", "Users.Create", null, "Create users");

        await grantRepository.InsertAsync(record, AbortToken);
        await definitionRepository.SaveAsync([group], [], [], [permission], [], [], AbortToken);
        var stored = await grantRepository.FindAsync("Users.Create", "Role", "admin", AbortToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(AbortToken);
        var storedPermissions = await definitionRepository.GetPermissionsListAsync(AbortToken);

        // then
        var names = new PermissionsStorageOptions();
        (await fixture.TableExistsAsync(_Schema, fixture.GrantsTable(), AbortToken)).Should().BeTrue();
        (
            await fixture.TableExistsAsync(
                _Schema,
                names.ResolvePermissionDefinitionsTableName(fixture.NamingStyle),
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        (
            await fixture.TableExistsAsync(
                _Schema,
                names.ResolvePermissionGroupDefinitionsTableName(fixture.NamingStyle),
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        stored.Should().NotBeNull();
        stored!.IsGranted.Should().BeTrue();
        storedGroups.Should().ContainSingle(x => x.Name == "Users");
        storedPermissions.Should().ContainSingle(x => x.Name == "Users.Create");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(101)]
    public async Task should_batch_permission_grants_from_single_use_enumerable(int count)
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        await fixture.CreateInsertCommandCounterAsync(_Schema, AbortToken);
        var records = Enumerable
            .Range(0, count)
            .Select(index => new PermissionGrantRecord(
                Guid.NewGuid(),
                $"Bulk.Permission.{index}",
                "Role",
                "bulk-admin",
                isGranted: true
            ))
            .ToArray();
        var enumerationCount = 0;

        IEnumerable<PermissionGrantRecord> enumerateOnce()
        {
            enumerationCount++;

            foreach (var record in records)
            {
                yield return record;
            }
        }

        // when
        await grantRepository.InsertManyAsync(enumerateOnce(), AbortToken);
        var stored = await grantRepository.GetListAsync("Role", "bulk-admin", AbortToken);

        // then
        enumerationCount.Should().Be(1);
        stored.Should().HaveCount(count);
        (await fixture.ReadInsertCommandCountAsync(_Schema, AbortToken)).Should().Be((count + 99) / 100);
    }

    [Fact]
    public async Task should_roll_back_all_permission_grants_when_later_batch_conflicts()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var records = Enumerable
            .Range(0, 100)
            .Select(index => new PermissionGrantRecord(
                Guid.NewGuid(),
                $"Atomic.Permission.{index}",
                "Role",
                "atomic-admin",
                isGranted: true
            ))
            .Append(
                new PermissionGrantRecord(
                    Guid.NewGuid(),
                    "Atomic.Permission.0",
                    "Role",
                    "atomic-admin",
                    isGranted: false
                )
            )
            .ToArray();

        // when
        var act = () => grantRepository.InsertManyAsync(records, AbortToken);

        // then
        (await act.Should().ThrowAsync<Exception>())
            .Which.Should()
            .Match<Exception>(x => RelationalFaults.IsUniqueViolation(x));
        (await grantRepository.GetListAsync("Role", "atomic-admin", AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_replace_a_grant_with_a_denial_in_one_save()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var granted = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Flip.Permission",
            "Role",
            "flip-admin",
            isGranted: true
        );
        await grantRepository.InsertAsync(granted, AbortToken);
        var denial = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Flip.Permission",
            "Role",
            "flip-admin",
            isGranted: false
        );

        // when — the denial reuses the grant's unique key, so the delete has to run first
        await grantRepository.SaveAsync([denial], [granted], AbortToken);

        // then
        var stored = await grantRepository.GetListAsync("Role", "flip-admin", AbortToken);
        stored.Should().ContainSingle().Which.Id.Should().Be(denial.Id);
        stored[0].IsGranted.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_the_deleted_grant_when_its_replacement_cannot_be_inserted()
    {
        // given — the replacement's id collides with another row, so its insert fails after the delete ran
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var granted = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Flip.Permission",
            "Role",
            "flip-admin",
            isGranted: true
        );
        var other = new PermissionGrantRecord(Guid.NewGuid(), "Flip.Other", "Role", "flip-admin", isGranted: true);
        await grantRepository.InsertManyAsync([granted, other], AbortToken);
        var denial = new PermissionGrantRecord(other.Id, "Flip.Permission", "Role", "flip-admin", isGranted: false);

        // when
        var act = () => grantRepository.SaveAsync([denial], [granted], AbortToken);

        // then — the delete rolled back with the insert, so the explicit grant is still there
        (await act.Should().ThrowAsync<Exception>())
            .Which.Should()
            .Match<Exception>(x => RelationalFaults.IsUniqueViolation(x));
        var stored = await grantRepository.FindAsync("Flip.Permission", "Role", "flip-admin", AbortToken);
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(granted.Id);
        stored.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_enumerate_permission_grants_when_insertion_is_already_cancelled()
    {
        // given
        using var host = fixture.CreateHost(_Schema);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var enumerated = false;

        IEnumerable<PermissionGrantRecord> records()
        {
            enumerated = true;
            yield return new PermissionGrantRecord(Guid.NewGuid(), "Cancelled", "Role", "admin", isGranted: true);
        }

        // when
        var act = () => grantRepository.InsertManyAsync(records(), cancellation.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        enumerated.Should().BeFalse();
    }

    [Theory]
    [InlineData("tenant-b")]
    [InlineData("Tenant-A")]
    public async Task should_scope_permission_grant_reads_to_current_tenant(string otherTenant)
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var currentTenant = new TestCurrentTenant("tenant-a");
        using var host = fixture.CreateHost(
            _Schema,
            configureServices: services => services.AddSingleton<ICurrentTenant>(currentTenant)
        );
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var tenantA = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Approve",
            "Role",
            "admin",
            isGranted: true,
            tenantId: "tenant-a"
        );
        var tenantB = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Approve",
            "Role",
            "admin",
            isGranted: false,
            tenantId: otherTenant
        );

        // when
        await grantRepository.InsertManyAsync([tenantA, tenantB], AbortToken);
        var found = await grantRepository.FindAsync("Users.Approve", "Role", "admin", AbortToken);
        var list = await grantRepository.GetListAsync("Role", "admin", AbortToken);

        // then
        found.Should().NotBeNull();
        found!.TenantId.Should().Be("tenant-a");
        found.IsGranted.Should().BeTrue();
        list.Should().ContainSingle(x => x.TenantId == "tenant-a");
    }

    [Fact]
    public async Task should_refuse_padded_raw_scope_keys_before_writing_any_enlisted_rows()
    {
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var tenant = new TestCurrentTenant("tenant-a");
        using var host = fixture.CreateHost(
            _Schema,
            configureServices: services => services.AddSingleton<ICurrentTenant>(tenant)
        );
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var stored = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Create",
            "Role",
            "acme",
            isGranted: true,
            tenantId: "tenant-a"
        );
        await repository.InsertAsync(stored, AbortToken);
        await using var connection = fixture.CreateConnection(fixture.ConnectionString);
        await using var unit = await fixture.BeginUnitOfWorkAsync(
            host.Services.GetRequiredService<IUnitOfWorkFactory>(),
            connection,
            AbortToken
        );
        var batchRow = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Delete",
            "Role",
            "acme",
            isGranted: true,
            tenantId: "tenant-a"
        );
        var padded = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Create",
            "Role",
            "acme",
            isGranted: true,
            tenantId: "tenant-a "
        );

        var readKeys = () => repository.GetListAsync(["Users.Create"], "Role", ["acme "], AbortToken);
        var insert = async () => await repository.InsertManyEnlistedAsync(unit, [batchRow, padded], AbortToken);
        await readKeys.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("providerKeys");
        await insert.Should().ThrowExactlyAsync<ArgumentException>();

        using (tenant.Change("tenant-a "))
        {
            var read = () => repository.FindAsync("Users.Create", "Role", "acme", AbortToken);
            var delete = async () => await repository.DeleteManyEnlistedAsync(unit, [stored], AbortToken);
            await read.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("tenantId");
            await delete.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("tenantId");
        }

        await unit.CompleteAsync(AbortToken);
        var grants = await repository.GetListAsync("Role", "acme", AbortToken);
        grants.Should().ContainSingle().Which.Id.Should().Be(stored.Id);
    }

    [Fact]
    public async Task should_refuse_a_padded_provider_key_and_leave_the_unpadded_grant_intact()
    {
        // given a stored grant for "acme"; SQL Server compares "acme " equal to it, PostgreSQL does not
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var paddedTenant = Substitute.For<ICurrentTenant>();
        paddedTenant.Id.Returns("t1 ");
        var store = new PermissionGrantStore(
            Substitute.For<IPermissionDefinitionManager>(),
            repository,
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            Substitute.For<ICache<PermissionGrantCacheItem>>(),
            paddedTenant,
            Options.Create(new PermissionManagementOptions()),
            NullLogger<PermissionGrantStore>.Instance
        );
        await repository.InsertAsync(
            new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", "Role", "acme", isGranted: true),
            AbortToken
        );

        // when
        var revoke = async () => await store.RevokeAsync("Users.Create", "Role", "acme ", AbortToken);
        var isGranted = async () => await store.IsGrantedAsync("Users.Create", "Role", "acme ", AbortToken);
        var grantToPaddedTenant = async () => await store.GrantAsync("Users.Delete", "Role", "acme", AbortToken);

        // then
        await revoke.Should().ThrowExactlyAsync<ArgumentException>();
        await isGranted.Should().ThrowExactlyAsync<ArgumentException>();
        await grantToPaddedTenant.Should().ThrowExactlyAsync<ArgumentException>();
        var stored = await repository.GetListAsync("Role", "acme", AbortToken);
        stored.Should().ContainSingle().Which.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_match_a_stored_prefix_when_a_lookup_value_is_longer_than_its_column()
    {
        // given a grant whose name and provider key fill their columns
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var grantRepository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var name = new string('n', PermissionGrantRecordConstants.NameMaxLength);
        var providerKey = new string('k', PermissionGrantRecordConstants.ProviderKeyMaxLength);
        await grantRepository.InsertAsync(
            new PermissionGrantRecord(Guid.NewGuid(), name, "Role", providerKey, isGranted: true),
            AbortToken
        );

        // when each lookup carries one character more than the column holds
        var found = await grantRepository.FindAsync(name + "x", "Role", providerKey, AbortToken);
        var byNames = await grantRepository.GetListAsync([name + "x"], "Role", providerKey, AbortToken);
        var byScope = await grantRepository.GetListAsync("Role", providerKey + "x", AbortToken);

        // then none of them matches the stored grant, which is the lookup value's prefix
        found.Should().BeNull();
        byNames.Should().BeEmpty();
        byScope.Should().BeEmpty();
    }

    [Fact]
    public async Task should_create_every_column_at_its_configured_length()
    {
        // given — a distinct length per option, so a column wired to the wrong option shows
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(
            _Schema,
            options =>
            {
                options.PermissionGrantsNameMaxLength = 201;
                options.PermissionGrantsProviderNameMaxLength = 202;
                options.PermissionGrantsProviderKeyMaxLength = 203;
                options.PermissionGrantsTenantIdMaxLength = 204;
                options.PermissionDefinitionsNameMaxLength = 205;
                options.PermissionDefinitionsDisplayNameMaxLength = 206;
                options.PermissionDefinitionsProvidersMaxLength = 207;
                options.PermissionGroupDefinitionsNameMaxLength = 208;
                options.PermissionGroupDefinitionsDisplayNameMaxLength = 209;
            }
        );

        // when
        await host.StartAsync(AbortToken);
        var lengths = await _ReadColumnLengthsAsync();

        // then
        lengths
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["permissiongrants.name"] = 201,
                    ["permissiongrants.providername"] = 202,
                    ["permissiongrants.providerkey"] = 203,
                    ["permissiongrants.tenantid"] = 204,
                    ["permissiondefinitions.name"] = 205,
                    ["permissiondefinitions.parentname"] = 205,
                    ["permissiondefinitions.displayname"] = 206,
                    ["permissiondefinitions.providers"] = 207,
                    ["permissiondefinitions.groupname"] = 208,
                    ["permissiongroupdefinitions.name"] = 208,
                    ["permissiongroupdefinitions.displayname"] = 209,
                }
            );
    }

    [Fact]
    public async Task should_grant_read_and_revoke_a_72_character_provider_key_when_its_column_is_widened_to_128()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema, options => options.PermissionGrantsProviderKeyMaxLength = 128);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var grant = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Create",
            "User",
            _LongProviderKey,
            isGranted: true
        );

        // when
        await repository.InsertAsync(grant, AbortToken);
        var found = await repository.FindAsync("Users.Create", "User", _LongProviderKey, AbortToken);
        var listed = await repository.GetListAsync("User", _LongProviderKey, AbortToken);
        await repository.DeleteAsync(grant, AbortToken);
        var afterRevoke = await repository.FindAsync("Users.Create", "User", _LongProviderKey, AbortToken);

        // then
        _LongProviderKey.Should().HaveLength(72);
        found.Should().NotBeNull();
        found!.ProviderKey.Should().Be(_LongProviderKey);
        listed.Should().ContainSingle().Which.Id.Should().Be(grant.Id);
        afterRevoke.Should().BeNull();
    }

    [Fact]
    public async Task should_reject_a_72_character_provider_key_at_the_default_length()
    {
        // given — the default stays 64, so an application that configures nothing keeps the schema it had
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(_Schema);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var grant = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Create",
            "User",
            _LongProviderKey,
            isGranted: true
        );

        // when
        var act = () => repository.InsertAsync(grant, AbortToken);

        // then
        (await act.Should().ThrowAsync<Exception>())
            .Which.Should()
            .BeAssignableTo(fixture.ProviderExceptionType);
    }

    // Keys are lower-cased with underscores removed, so one expectation reads on either naming style.
    [Fact]
    public async Task should_refuse_a_versioned_save_after_another_writer_changed_the_group()
    {
        // given a role's group read with its version, then one permission prohibited by someone else
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var tenant = new TestCurrentTenant("tenant-a");
        using var host = _CreateTenantHost(tenant);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host, tenant);
        await store.GrantAsync(["Users.Create", "Users.Delete"], "Role", "admin", AbortToken);
        var read = await store.GetVersionedAsync(["Users.Create", "Users.Delete"], "Role", "admin", AbortToken);
        await store.RevokeAsync("Users.Delete", "Role", "admin", AbortToken);

        // when the stale form clears the first permission
        var act = () =>
            store.SetVersionedAsync(
                _Grants(("Users.Create", null), ("Users.Delete", true)),
                "Role",
                "admin",
                read.Version,
                AbortToken
            );

        // then
        await act.Should().ThrowExactlyAsync<DBConcurrencyException>();
        var stored = await store.GetVersionedAsync(["Users.Create", "Users.Delete"], "Role", "admin", AbortToken);
        stored.Grants.Should().BeEquivalentTo(_Grants(("Users.Create", true), ("Users.Delete", false)));
        stored.Version.Should().NotBe(read.Version);
    }

    [Fact]
    public async Task should_save_a_current_versioned_group_and_give_it_a_new_version()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var tenant = new TestCurrentTenant("tenant-a");
        using var host = _CreateTenantHost(tenant);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host, tenant);
        await store.GrantAsync(["Users.Create", "Users.Delete"], "Role", "admin", AbortToken);
        var read = await store.GetVersionedAsync(
            ["Users.Create", "Users.Delete", "Users.Export"],
            "Role",
            "admin",
            AbortToken
        );

        // when the form keeps one grant, clears one, and prohibits one
        var committedVersion = await store.SetVersionedAsync(
            _Grants(("Users.Create", true), ("Users.Delete", null), ("Users.Export", false)),
            "Role",
            "admin",
            read.Version,
            AbortToken
        );
        var replay = () =>
            store.SetVersionedAsync(_Grants(("Users.Create", false)), "Role", "admin", read.Version, AbortToken);

        // then
        var stored = await store.GetVersionedAsync(
            ["Users.Create", "Users.Delete", "Users.Export"],
            "Role",
            "admin",
            AbortToken
        );
        stored
            .Grants.Should()
            .BeEquivalentTo(_Grants(("Users.Create", true), ("Users.Delete", null), ("Users.Export", false)));
        stored.Version.Should().NotBe(read.Version);
        committedVersion.Should().Be(stored.Version);
        await replay.Should().ThrowExactlyAsync<DBConcurrencyException>();
    }

    [Fact]
    public async Task should_give_each_target_and_tenant_its_own_version_when_nothing_is_stored()
    {
        // given
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var tenant = new TestCurrentTenant("tenant-a");
        using var host = _CreateTenantHost(tenant);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host, tenant);

        // when
        var admin = await store.GetVersionedAsync(["Users.Create"], "Role", "admin", AbortToken);
        var editor = await store.GetVersionedAsync(["Users.Create"], "Role", "editor", AbortToken);
        VersionedPermissionGrants otherTenant;

        using (tenant.Change("tenant-b"))
        {
            otherTenant = await store.GetVersionedAsync(["Users.Create"], "Role", "admin", AbortToken);
        }

        // then a version read for one tenant never authorizes a save in another
        new[] { admin.Version, editor.Version, otherTenant.Version }
            .Should()
            .OnlyHaveUniqueItems();
        var crossTenantSave = () =>
            store.SetVersionedAsync(_Grants(("Users.Create", true)), "Role", "admin", otherTenant.Version, AbortToken);
        await crossTenantSave.Should().ThrowExactlyAsync<DBConcurrencyException>();
    }

    [Fact]
    public async Task should_let_exactly_one_of_two_saves_from_the_same_version_succeed()
    {
        // given two forms read the same empty group; each grants a different permission, a pair of writes that touch
        // no common row
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var tenant = new TestCurrentTenant("tenant-a");
        using var host = _CreateTenantHost(tenant);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host, tenant);
        var read = await store.GetVersionedAsync(["Users.Create", "Users.Delete"], "Role", "admin", AbortToken);

        // when both save at once
        var results = await Task.WhenAll(
            _TrySaveAsync(store, _Grants(("Users.Create", true), ("Users.Delete", null)), read.Version),
            _TrySaveAsync(store, _Grants(("Users.Create", null), ("Users.Delete", true)), read.Version)
        );

        // then
        results.Should().ContainSingle(saved => saved);
        var stored = await store.GetVersionedAsync(["Users.Create", "Users.Delete"], "Role", "admin", AbortToken);
        stored.Grants.Values.Count(x => x is not null).Should().Be(1);
    }

    [Fact]
    public async Task should_preserve_an_unchanged_version_and_accept_an_absent_set_cleared_version()
    {
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var tenant = new TestCurrentTenant("tenant-a");
        using var host = _CreateTenantHost(tenant);
        await host.StartAsync(AbortToken);
        var store = _CreateStore(host, tenant);
        var absent = await store.GetVersionedAsync(["Users.Create", "Users.Delete"], "Role", "admin", AbortToken);

        var added = await store.SetVersionedAsync(
            _Grants(("Users.Create", true), ("Users.Delete", null)),
            "Role",
            "admin",
            absent.Version,
            AbortToken
        );
        var unchanged = await store.SetVersionedAsync(
            _Grants(("Users.Create", true), ("Users.Delete", null)),
            "Role",
            "admin",
            added,
            AbortToken
        );
        unchanged.Should().Be(added);
        var cleared = await store.SetVersionedAsync(
            _Grants(("Users.Create", null), ("Users.Delete", null)),
            "Role",
            "admin",
            unchanged,
            AbortToken
        );
        cleared.Should().Be(absent.Version);

        var restored = await store.SetVersionedAsync(
            _Grants(("Users.Create", true), ("Users.Delete", null)),
            "Role",
            "admin",
            absent.Version,
            AbortToken
        );
        var current = await store.GetVersionedAsync(["Users.Create", "Users.Delete"], "Role", "admin", AbortToken);
        restored.Should().Be(current.Version);
    }

    private IHost _CreateTenantHost(TestCurrentTenant tenant)
    {
        return fixture.CreateHost(
            _Schema,
            configureServices: services => services.AddSingleton<ICurrentTenant>(tenant)
        );
    }

    private static PermissionGrantStore _CreateStore(IHost host, ICurrentTenant tenant)
    {
        // The storage-only host does not register the definition manager's dependencies, and versioned reads and
        // writes never consult definitions.
        return new PermissionGrantStore(
            Substitute.For<IPermissionDefinitionManager>(),
            host.Services.GetRequiredService<IPermissionGrantRepository>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            host.Services.GetRequiredService<ICache<PermissionGrantCacheItem>>(),
            tenant,
            Options.Create(new PermissionManagementOptions()),
            NullLogger<PermissionGrantStore>.Instance
        );
    }

    private async Task<bool> _TrySaveAsync(PermissionGrantStore store, Dictionary<string, bool?> grants, string version)
    {
        try
        {
            await store.SetVersionedAsync(grants, "Role", "admin", version, AbortToken);

            return true;
        }
        catch (DBConcurrencyException)
        {
            return false;
        }
    }

    private static Dictionary<string, bool?> _Grants(params (string Name, bool? IsGranted)[] grants)
    {
        return grants.ToDictionary(x => x.Name, x => x.IsGranted, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, int>> _ReadColumnLengthsAsync()
    {
        await using var connection = fixture.CreateConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TABLE_NAME, COLUMN_NAME, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = '{_Schema}' AND CHARACTER_MAXIMUM_LENGTH > 0
            """;
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var lengths = new Dictionary<string, int>(StringComparer.Ordinal);

        while (await reader.ReadAsync(AbortToken))
        {
            var table = reader.GetString(0);

            if (!table.StartsWith(SchemaRunner.HistoryTableName, StringComparison.Ordinal))
            {
                lengths[_Normalize(table) + "." + _Normalize(reader.GetString(1))] = reader.GetInt32(2);
            }
        }

        return lengths;
    }

    private static string _Normalize(string name)
    {
        return name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static IEnumerable<string> _WithoutHistory(IEnumerable<string> objects)
    {
        return objects.Where(x => !x.StartsWith(SchemaRunner.HistoryTableName + ".", StringComparison.Ordinal));
    }

    private static (HashSet<string> Columns, HashSet<string> Indexes) _MappedObjects(IModel model)
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

        return (columns, indexes);
    }

    private sealed class ParityDbContext(DbContextOptions<ParityDbContext> options, PermissionsStorageOptions storage)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessPermissions(
                storage,
                HeadlessStorageNaming.ForProvider(Database.ProviderName)
            );
        }
    }

    private sealed class TestCurrentTenant(string? tenantId) : ICurrentTenant
    {
        public bool IsAvailable => Id is not null;

        public string? Id { get; private set; } = tenantId;

        public string? Name => null;

        public IDisposable Change(string? id, string? name = null)
        {
            var previousId = Id;
            Id = id;

            return new RestoreAction(() => Id = previousId);
        }
    }

    private sealed class RestoreAction(Action action) : IDisposable
    {
        public void Dispose()
        {
            action();
        }
    }
}
