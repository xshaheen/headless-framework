using Framework.Abstractions;
using Framework.Domains;
using Framework.Orm.EntityFramework;
using Framework.Orm.EntityFramework.Contexts;
using Framework.Primitives;
using Framework.Testing.Helpers;
using Meziantou.Extensions.Logging.Xunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[MustDisposeResource]
public sealed class HeadlessDbContextTests : IDisposable
{
    private readonly UserId _userId;
    private readonly DateTimeOffset _now;

    private readonly TestClock _clock = new();
    private readonly TestCurrentTenant _currentTenant = new();
    private readonly TestCurrentUser _currentUser = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly SqliteConnection _sqliteConnection;
    private readonly XUnitLoggerProvider _xUnitLoggerProvider;

    public HeadlessDbContextTests(ITestOutputHelper output)
    {
        const string datasourceMemory = "DataSource=:memory:";

        var services = new ServiceCollection();

        _xUnitLoggerProvider = new XUnitLoggerProvider(
            output,
            new XUnitLoggerOptions
            {
                IncludeLogLevel = false,
                IncludeScopes = true,
                IncludeCategory = false,
            }
        );

        services.AddLogging(x => x.AddProvider(_xUnitLoggerProvider));
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ICurrentTenant>(_currentTenant);
        services.AddSingleton<ICurrentUser>(_currentUser);
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();

        // Create and open a single SQLite connection for the test class lifetime
        _sqliteConnection = new SqliteConnection(datasourceMemory);
        _sqliteConnection.Open();
        services.AddSingleton(_sqliteConnection);
        services.AddHeadlessDbContext<TestDb>(options => options.UseSqlite(_sqliteConnection));

        _serviceProvider = services.BuildServiceProvider();
        _userId = new UserId(Guid.NewGuid().ToString());
        _currentUser.UserId = _userId;
        _now = new DateTimeOffset(2025, 1, 2, 12, 0, 0, TimeSpan.Zero);
        _clock.TimeProvider = new FakeTimeProvider(_now);
        using var scope = _serviceProvider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<TestDb>();
        db.Database.EnsureCreated();
        db.Database.Migrate();
    }

    public void Dispose()
    {
        _xUnitLoggerProvider.Dispose();
        _serviceProvider.Dispose();
        _sqliteConnection.Dispose();
    }

    [Fact]
    public async Task save_changes_without_emitters_should_not_publish_messages()
    {
        // given
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestDb>();

        var entity = new BasicEntity { Name = "no-op" };
        db.Basics.Add(entity);

        // when
        await db.SaveChangesAsync();

        // then
        var basicCount = await db.Basics.CountAsync();
        basicCount.Should().Be(1);
        db.EmittedLocalMessages.Should().BeEmpty();
        db.EmittedDistributedMessages.Should().BeEmpty();
    }

    // Add

    [Fact]
    public async Task save_changes_add_should_set_guid_id_create_audit_and_concurrency_stamp_and_emit_local_messages()
    {
        // given
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestDb>();

        var entity = new TestEntity { Name = "created", TenantId = "T1" };

        db.Tests.Add(entity);

        // when
        await db.SaveChangesAsync();

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
        db.EmittedLocalMessages.Should().ContainSingle();
        var local = db.EmittedLocalMessages.Single();
        local.Emitter.Should().Be(entity);
        local.Messages.Should().HaveCount(2);
        var createdMessage = local.Messages.OfType<EntityCreatedEventData<TestEntity>>().Single();
        createdMessage.Entity.Should().Be(entity);
        var changedMessage = local.Messages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Update

    [Fact]
    public async Task save_changes_update_should_set_update_audit_and_update_concurrency_stamp_and_emit_updated_message()
    {
        // given
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestDb>();

        var entity = new TestEntity { Name = "initial", TenantId = "T1" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync();
        var oldStamp = entity.ConcurrencyStamp;

        // when
        entity.Name = "updated";
        await db.SaveChangesAsync();

        // then
        entity.DateUpdated.Should().Be(_now);
        entity.UpdatedById.Should().Be(_userId);
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.ConcurrencyStamp.Should().NotBe(oldStamp);

        // Local messages: Updated + Changed
        db.EmittedLocalMessages.Should().NotBeEmpty();
        var last = db.EmittedLocalMessages[^1];
        last.Messages.Should().HaveCount(2);
        var updatedMessage = last.Messages.OfType<EntityUpdatedEventData<TestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = last.Messages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Delete

    [Fact]
    public async Task save_changes_soft_delete_should_set_delete_audit_and_emit_deleted_message()
    {
        // given
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestDb>();

        var entity = new TestEntity { Name = "to-delete", TenantId = "T1" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync();

        // when
        entity.MarkDeleted();
        db.Update(entity);
        await db.SaveChangesAsync();

        // then
        entity.IsDeleted.Should().BeTrue();
        entity.DateDeleted.Should().Be(_now);
        entity.DeletedById.Should().Be(_userId);

        var last = db.EmittedLocalMessages[^1];
        last.Messages.Should().HaveCount(2);
        var updatedMessage = last.Messages.OfType<EntityUpdatedEventData<TestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = last.Messages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Suspend

    [Fact]
    public async Task save_changes_suspend_should_set_suspend_audit()
    {
        // given
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestDb>();

        var entity = new TestEntity { Name = "to-suspend", TenantId = "T1" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync();

        // when
        entity.MarkSuspended();
        db.Update(entity);
        await db.SaveChangesAsync();

        // then
        entity.IsSuspended.Should().BeTrue();
        entity.DateSuspended.Should().Be(_now);
        entity.SuspendedById.Should().Be(_userId);
    }

    // Publish messages

    [Fact]
    public async Task distributed_and_local_messages_should_publish_within_existing_transaction()
    {
        // given
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestDb>();

        var entity = new TestEntity { Name = "with-msgs", TenantId = "T1" };
        entity.AddMessage(new TestDistributedMessage("hello"));
        db.Tests.Add(entity);

        await using var tx = await db.Database.BeginTransactionAsync();

        // when
        await db.SaveChangesAsync();

        // then
        db.EmittedLocalMessages.Should().NotBeEmpty();
        db.EmittedDistributedMessages.Should().ContainSingle();
        var dist = db.EmittedDistributedMessages.Single();
        dist.Emitter.Should().Be(entity);
        dist.Messages.Should().ContainSingle();
        dist.Messages.Single().Should().BeOfType<TestDistributedMessage>();

        await tx.CommitAsync();
    }

    // ExecuteTransactionAsync

    [Fact]
    public async Task execute_transaction_async_should_commit_or_rollback_by_return_value()
    {
        // commit path
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestDb>();

        var committed = false;

        await db.ExecuteTransactionAsync(async () =>
        {
            await db.Basics.AddAsync(new BasicEntity { Name = "in-tx" });
            committed = true;

            return true;
        });

        (await db.Basics.CountAsync()).Should().Be(1);
        committed.Should().BeTrue();

        // rollback path
        await db.ExecuteTransactionAsync(async () =>
        {
            await db.Basics.AddAsync(new BasicEntity { Name = "rolled" });

            return false;
        });

        (await db.Basics.CountAsync()).Should().Be(1);
    }

    // Global filters

    [Fact]
    public async Task global_filters_should_filter_by_tenant_delete_and_suspend_flags_and_can_be_disabled()
    {
        // given
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestDb>();

        var a = new TestEntity { Name = "a", TenantId = "TENANT-1" };
        var b = new TestEntity { Name = "b", TenantId = "TENANT-2" };
        var c = new TestEntity { Name = "c", TenantId = "TENANT-1" };
        var d = new TestEntity { Name = "d", TenantId = "TENANT-1" };
        var x = new TestEntity { Name = "x", TenantId = null };
        await db.Tests.AddRangeAsync(a, b, c, d, x);
        await db.SaveChangesAsync();

        // soft delete c
        c.MarkDeleted();
        // suspend d
        d.MarkSuspended();
        db.UpdateRange(c, d);
        await db.SaveChangesAsync();

        // when/then: default filters on
        using (_currentTenant.Change(null))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync();
            items.Should().BeEquivalentTo("x");
        }

        using (_currentTenant.Change("TENANT-1"))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync();
            items.Should().BeEquivalentTo("a");
        }

        // disable delete filter -> suspended still filtered
        using (db.FilterStatus.ChangeDeleteFilterEnabled(false))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync();
            items.Should().BeEquivalentTo("a", "c");
        }

        using (db.FilterStatus.ChangeTenantFilterEnabled(false))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync();
            items.Should().BeEquivalentTo("a", "b", "x");
        }

        // disable suspended filter -> deleted still filtered
        using (db.FilterStatus.ChangeSuspendedFilterEnabled(false))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync();
            items.Should().BeEquivalentTo("a");
        }

        // disable tenant filter -> still filters by delete/suspend
        using (db.FilterStatus.ChangeTenantFilterEnabled(false))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync();
            items.Should().BeEquivalentTo("a", "b");
        }

        // disable all -> all visible
        using (db.FilterStatus.ChangeTenantFilterEnabled(false))
        using (db.FilterStatus.ChangeDeleteFilterEnabled(false))
        using (db.FilterStatus.ChangeSuspendedFilterEnabled(false))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync();
            items.Should().BeEquivalentTo("a", "b", "c", "d");
        }
    }

    [MustDisposeResource]
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

        public string? TenantId { get; init; }

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
