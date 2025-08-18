using System;
using System.Linq;
using FluentAssertions;
using Framework.Abstractions;
using Framework.Domains;
using Framework.Orm.EntityFramework.Contexts;
using Framework.Primitives;
using Framework.Testing.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Tests;

public sealed class DbContextBaseTests : IDisposable
{
    private readonly UserId _userId;
    private readonly DateTimeOffset _now;

    private readonly TestDb _db;
    private readonly SqliteConnection _connection;
    private readonly TestClock _clock = new();
    private readonly TestCurrentTenant _currentTenant = new();
    private readonly TestCurrentUser _currentUser = new();
    private readonly SequentialAsStringGuidGenerator _guidGenerator = new();

    public DbContextBaseTests()
    {
        _userId = new UserId(Guid.NewGuid().ToString());
        _currentUser.UserId = _userId;

        _now = new DateTimeOffset(2025, 1, 2, 12, 0, 0, TimeSpan.Zero);
        _clock.TimeProvider = new FakeTimeProvider(_now);

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbContextOptions = new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options;
        _db = new TestDb(_currentUser, _currentTenant, _guidGenerator, _clock, dbContextOptions);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task save_changes_without_emitters_should_not_publish_messages()
    {
        // given
        var entity = new BasicEntity { Name = "no-op" };
        _db.Basics.Add(entity);

        // when
        await _db.SaveChangesAsync();

        // then
        var basicCount = await _db.Basics.CountAsync();
        basicCount.Should().Be(1);
        _db.EmittedLocalMessages.Should().BeEmpty();
        _db.EmittedDistributedMessages.Should().BeEmpty();
    }

    // Add

    [Fact]
    public void save_changes_add_should_set_guid_id_create_audit_and_concurrency_stamp_and_emit_local_messages()
    {
        // given
        var entity = new TestEntity { Name = "created", TenantId = "T1" };

        _db.Tests.Add(entity);

        // when
        _db.SaveChanges();

        // then
        entity.Id.Should().NotBe(Guid.Empty);
        entity.DateCreated.Should().Be(_now);
        entity.CreatedById.Should().Be(_userId);
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.DateUpdated.Should().BeNull();
        entity.UpdatedById.Should().BeNull();
        entity.IsDeleted.Should().BeFalse();
        entity.DateDeleted.Should().BeNull();
        entity.DeletedById.Should().BeNull();
        entity.IsSuspended.Should().BeFalse();
        entity.DateSuspended.Should().BeNull();
        entity.SuspendedById.Should().BeNull();

        // Local messages: Created + Changed
        _db.EmittedLocalMessages.Should().ContainSingle();
        var local = _db.EmittedLocalMessages.Single();
        local.Emitter.Should().Be(entity);
        local.Messages.Should().HaveCount(2);
        var createdMessage = local.Messages.OfType<EntityCreatedEventData<TestEntity>>().Single();
        createdMessage.Entity.Should().Be(entity);
        var changedMessage = local.Messages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Update

    [Fact]
    public void save_changes_update_should_set_update_audit_and_update_concurrency_stamp_and_emit_updated_message()
    {
        // given

        var entity = new TestEntity { Name = "initial", TenantId = "T1" };
        _db.Tests.Add(entity);
        _db.SaveChanges();
        var oldStamp = entity.ConcurrencyStamp;

        // when
        entity.Name = "updated";
        _db.SaveChanges();

        // then
        entity.DateUpdated.Should().Be(_now);
        entity.UpdatedById.Should().Be(_userId);
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.ConcurrencyStamp.Should().NotBe(oldStamp);

        // Local messages: Updated + Changed
        _db.EmittedLocalMessages.Should().NotBeEmpty();
        var last = _db.EmittedLocalMessages[^1];
        last.Messages.Should().HaveCount(2);
        var updatedMessage = last.Messages.OfType<EntityUpdatedEventData<TestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = last.Messages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Delete

    [Fact]
    public void save_changes_soft_delete_should_set_delete_audit_and_emit_deleted_message()
    {
        // given
        var entity = new TestEntity { Name = "to-delete", TenantId = "T1" };
        _db.Tests.Add(entity);
        _db.SaveChanges();

        // when
        entity.MarkDeleted();
        _db.Update(entity);
        _db.SaveChanges();

        // then
        entity.IsDeleted.Should().BeTrue();
        entity.DateDeleted.Should().Be(_now);
        entity.DeletedById.Should().Be(_userId);

        var evt = _db.EmittedLocalMessages[^1];
        evt.Messages.Should().ContainSingle(m => m is EntityDeletedEventData<TestEntity>);
    }

    // Suspend

    [Fact]
    public void save_changes_suspend_should_set_suspend_audit()
    {
        // given
        var entity = new TestEntity { Name = "to-suspend", TenantId = "T1" };
        _db.Tests.Add(entity);
        _db.SaveChanges();

        // when
        entity.MarkSuspended();
        _db.Update(entity);
        _db.SaveChanges();

        // then
        entity.IsSuspended.Should().BeTrue();
        entity.DateSuspended.Should().Be(_now);
        entity.SuspendedById.Should().Be(_userId);
    }

    // Publish messages

    [Fact]
    public void distributed_and_local_messages_should_publish_within_existing_transaction()
    {
        // given
        var entity = new TestEntity { Name = "with-msgs", TenantId = "T1" };
        entity.AddMessage(new TestDistributedMessage("hello"));
        _db.Tests.Add(entity);

        using var tx = _db.Database.BeginTransaction();

        // when
        _db.SaveChanges();

        // then
        _db.EmittedLocalMessages.Should().NotBeEmpty();
        _db.EmittedDistributedMessages.Should().ContainSingle();
        var dist = _db.EmittedDistributedMessages.Single();
        dist.Emitter.Should().Be(entity);
        dist.Messages.Should().ContainSingle();
        dist.Messages.Single().Should().BeOfType<TestDistributedMessage>();

        tx.Commit();
    }

    // ExecuteTransactionAsync

    [Fact]
    public async Task execute_transaction_async_should_commit_or_rollback_by_return_value()
    {
        // commit path
        var committed = false;

        await _db.ExecuteTransactionAsync(async () =>
        {
            await _db.Basics.AddAsync(new BasicEntity { Name = "in-tx" });
            committed = true;

            return true;
        });

        (await _db.Basics.CountAsync()).Should().Be(1);
        committed.Should().BeTrue();

        // rollback path
        await _db.ExecuteTransactionAsync(async () =>
        {
            await _db.Basics.AddAsync(new BasicEntity { Name = "rolled" });

            return false;
        });

        (await _db.Basics.CountAsync()).Should().Be(1);
    }

    // Global filters

    [Fact]
    public void global_filters_should_filter_by_tenant_delete_and_suspend_flags_and_can_be_disabled()
    {
        // given
        var a = new TestEntity { Name = "a", TenantId = "TENANT-1" };
        var b = new TestEntity { Name = "b", TenantId = "TENANT-2" };
        var c = new TestEntity { Name = "c", TenantId = "TENANT-1" };
        var d = new TestEntity { Name = "d", TenantId = "TENANT-1" };
        _db.Tests.AddRange(a, b, c, d);
        _db.SaveChanges();

        // soft delete c
        c.MarkDeleted();
        // suspend d
        d.MarkSuspended();
        _db.UpdateRange(c, d);
        _db.SaveChanges();

        // when/then: default filters on
        _currentTenant.Change("TENANT-1");
        var items = _db.Tests.Select(x => x.Name).ToArray();
        items.Should().BeEquivalentTo("a");

        _currentTenant.Change(null);
        items = _db.Tests.Select(x => x.Name).ToArray();
        items.Should().BeEquivalentTo("a", "b");

        // disable delete filter -> suspended still filtered
        using (_db.FilterStatus.ChangeDeleteFilterEnabled(false))
        {
            items = _db.Tests.Select(x => x.Name).ToArray();
            items.Should().BeEquivalentTo("a", "c");
        }

        // disable suspended filter -> deleted still filtered
        using (_db.FilterStatus.ChangeSuspendedFilterEnabled(false))
        {
            items = _db.Tests.Select(x => x.Name).ToArray();
            items.Should().BeEquivalentTo("a");
        }

        // disable tenant filter -> still filters by delete/suspend
        using (_db.FilterStatus.ChangeTenantFilterEnabled(false))
        {
            items = _db.Tests.Select(x => x.Name).ToArray();
            items.Should().BeEquivalentTo("a", "b");
        }

        // disable all -> all visible
        using (_db.FilterStatus.ChangeTenantFilterEnabled(false))
        using (_db.FilterStatus.ChangeDeleteFilterEnabled(false))
        using (_db.FilterStatus.ChangeSuspendedFilterEnabled(false))
        {
            items = _db.Tests.Select(x => x.Name).ToArray();
            items.Should().BeEquivalentTo("a", "b", "c", "d");
        }
    }

    private sealed class TestDb(
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IClock clock,
        DbContextOptions options
    ) : HeadlessDbContext(currentUser, currentTenant, guidGenerator, clock, options)
    {
        public DbSet<TestEntity> Tests { get; set; }

        public DbSet<BasicEntity> Basics { get; set; }

        public List<EmitterDistributedMessages> EmittedDistributedMessages { get; } = [];

        public List<EmitterLocalMessages> EmittedLocalMessages { get; } = [];

        public override string DefaultSchema => "dbo";

        protected override Task PublishMessagesAsync(
            List<EmitterDistributedMessages> emitters,
            IDbContextTransaction currentTransaction,
            CancellationToken cancellationToken
        )
        {
            EmittedDistributedMessages.AddRange(emitters);

            return Task.CompletedTask;
        }

        protected override void PublishMessages(
            List<EmitterDistributedMessages> emitters,
            IDbContextTransaction currentTransaction
        )
        {
            EmittedDistributedMessages.AddRange(emitters);
        }

        protected override Task PublishMessagesAsync(
            List<EmitterLocalMessages> emitters,
            CancellationToken cancellationToken
        )
        {
            EmittedLocalMessages.AddRange(emitters);

            return Task.CompletedTask;
        }

        protected override void PublishMessages(List<EmitterLocalMessages> emitters)
        {
            EmittedLocalMessages.AddRange(emitters);
        }
    }

    private sealed class TestEntity
        : AggregateRoot,
            IEntity<Guid>,
            ICreateAudit<UserId>,
            IUpdateAudit<UserId>,
            IDeleteAudit<UserId>,
            ISuspendAudit<UserId>,
            IHasConcurrencyStamp,
            IMultiTenant
    {
        public Guid Id { get; private init; }

        public required string Name { get; set; }

        public TenantId TenantId { get; init; } = "";

        // Audits
        public DateTimeOffset DateCreated { get; private init; }

        public UserId? CreatedById { get; private init; }

        public DateTimeOffset? DateUpdated { get; private init; }

        public UserId? UpdatedById { get; private init; }

        public bool IsDeleted { get; private set; }

        public DateTimeOffset? DateDeleted { get; private init; }

        public DateTimeOffset? DateRestored { get; private init; }

        public UserId? DeletedById { get; private init; }

        public UserId? RestoredById { get; private init; }

        public bool IsSuspended { get; private set; }

        public DateTimeOffset? DateSuspended { get; private init; }

        public UserId? SuspendedById { get; private init; }

        // Concurrency
        public string? ConcurrencyStamp { get; private init; }

        // Domain helpers to toggle flags so EF tracks modifications
        public void MarkDeleted() => IsDeleted = true;

        public void MarkSuspended() => IsSuspended = true;

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    private sealed class BasicEntity : IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public IReadOnlyList<object> GetKeys() => [Id];
    }

    private sealed record TestDistributedMessage(string Text) : IDistributedMessage
    {
        public string UniqueId { get; } = Guid.NewGuid().ToString("N");

        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
