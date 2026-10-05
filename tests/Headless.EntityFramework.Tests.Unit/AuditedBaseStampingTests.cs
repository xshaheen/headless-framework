// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Primitives;
using Headless.Testing;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
public sealed class AuditedBaseStampingTests : TestBase
{
    private static readonly DateTimeOffset _Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_Start);
    private readonly TestCurrentUser _currentUser = new() { UserId = "user-1", IsAuthenticated = true };

    public static TheoryData<string> Rows =>
        [
            nameof(AuditedNote),
            nameof(AuditedLedger),
            nameof(SuspendableNote),
            nameof(SuspendableLedger),
            nameof(DeletableNote),
            nameof(DeletableLedger),
        ];

    public static TheoryData<string> SuspendableRows => [nameof(SuspendableNote), nameof(SuspendableLedger)];

    public static TheoryData<string> DeletableRows => [nameof(DeletableNote), nameof(DeletableLedger)];

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task should_stamp_create_audit_when_audited_base_added(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = _Create(kind);
        db.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(entity);
        saved.CreatedAt.Should().Be(_Start);
        saved.CreatedById.Should().Be(_currentUser.UserId);
        saved.UpdatedAt.Should().BeNull();
        saved.UpdatedById.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task should_stamp_latest_updater_when_audited_base_modified_by_successive_users(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = _Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _currentUser.UserId = "user-2";
        entity.Rename("first rename");
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _currentUser.UserId = "user-3";

        // when
        entity.Rename("second rename");
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(entity);
        saved.CreatedAt.Should().Be(_Start);
        saved.CreatedById.Should().Be((UserId)"user-1");
        saved.UpdatedAt.Should().Be(_Start.AddMinutes(10));
        saved.UpdatedById.Should().Be((UserId)"user-3");
    }

    [Fact]
    public async Task should_keep_explicit_update_audit_when_update_called()
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        db.Add(new TestAccount { Id = "user-1" });
        db.Add(new TestAccount { Id = "owner" });
        var document = new AuditedDocument { Id = Guid.CreateVersion7(), Name = "doc" };
        db.Add(document);
        await db.SaveChangesAsync(AbortToken);
        var requestStartedAt = _Start.AddSeconds(30);
        _clock.Advance(TimeSpan.FromMinutes(5));

        // when
        document.Name = "renamed";
        document.Update(requestStartedAt, "owner");
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(document);
        saved.UpdatedAt.Should().Be(requestStartedAt);
        saved.UpdatedById.Should().Be((UserId)"owner");
    }

    [Theory]
    [MemberData(nameof(DeletableRows))]
    public async Task should_stamp_delete_audit_when_audited_base_soft_deleted(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = (IDeletableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));

        // when
        entity.SoftDelete();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = (IDeletableRow)await harness.ReloadAsync((IAuditedRow)entity);
        saved.IsDeleted.Should().BeTrue();
        saved.DeletedAt.Should().Be(_Start.AddMinutes(5));
        saved.DeletedById.Should().Be(_currentUser.UserId);
        saved.RestoredAt.Should().BeNull();
        saved.RestoredById.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(DeletableRows))]
    public async Task should_keep_deletion_and_stamp_restoration_when_audited_base_restored(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = (IDeletableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        entity.SoftDelete();
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _currentUser.UserId = "user-2";

        // when
        entity.Undelete();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = (IDeletableRow)await harness.ReloadAsync((IAuditedRow)entity);
        saved.IsDeleted.Should().BeFalse();
        saved.DeletedAt.Should().Be(_Start.AddMinutes(5));
        saved.DeletedById.Should().Be((UserId)"user-1");
        saved.RestoredAt.Should().Be(_Start.AddMinutes(10));
        saved.RestoredById.Should().Be((UserId)"user-2");
    }

    [Theory]
    [MemberData(nameof(DeletableRows))]
    public async Task should_replace_previous_deletion_when_audited_base_deleted_again(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = (IDeletableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        entity.SoftDelete();
        await db.SaveChangesAsync(AbortToken);
        entity.Undelete();
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _currentUser.UserId = "user-3";

        // when
        entity.SoftDelete();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = (IDeletableRow)await harness.ReloadAsync((IAuditedRow)entity);
        saved.IsDeleted.Should().BeTrue();
        saved.DeletedAt.Should().Be(_Start.AddMinutes(5));
        saved.DeletedById.Should().Be((UserId)"user-3");
        saved.RestoredAt.Should().Be(_Start);
        saved.RestoredById.Should().Be((UserId)"user-1");
    }

    [Theory]
    [MemberData(nameof(SuspendableRows))]
    public async Task should_stamp_suspend_audit_when_audited_base_suspended(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = (ISuspendableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));

        // when
        entity.Freeze();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = (ISuspendableRow)await harness.ReloadAsync((IAuditedRow)entity);
        saved.IsSuspended.Should().BeTrue();
        saved.SuspendedAt.Should().Be(_Start.AddMinutes(5));
        saved.SuspendedById.Should().Be(_currentUser.UserId);
    }

    [Theory]
    [MemberData(nameof(SuspendableRows))]
    public async Task should_keep_suspension_and_stamp_unsuspension_when_audited_base_unsuspended(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = (ISuspendableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        entity.Freeze();
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _currentUser.UserId = "user-2";

        // when
        entity.Unfreeze();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = (ISuspendableRow)await harness.ReloadAsync((IAuditedRow)entity);
        saved.IsSuspended.Should().BeFalse();
        saved.SuspendedAt.Should().Be(_Start.AddMinutes(5));
        saved.SuspendedById.Should().Be((UserId)"user-1");
        saved.UnsuspendedAt.Should().Be(_Start.AddMinutes(10));
        saved.UnsuspendedById.Should().Be((UserId)"user-2");
    }

    [Theory]
    [MemberData(nameof(SuspendableRows))]
    public async Task should_record_null_actor_when_audited_base_unsuspended_and_resuspended_anonymously(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = (ISuspendableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        entity.Freeze();
        await db.SaveChangesAsync(AbortToken);
        _SignOut();

        // when
        entity.Unfreeze();
        await db.SaveChangesAsync(AbortToken);
        var unsuspended = (ISuspendableRow)await harness.ReloadAsync((IAuditedRow)entity);
        entity.Freeze();
        await db.SaveChangesAsync(AbortToken);
        var resuspended = (ISuspendableRow)await harness.ReloadAsync((IAuditedRow)entity);

        // then
        unsuspended.UnsuspendedById.Should().BeNull();
        unsuspended.SuspendedById.Should().Be((UserId)"user-1");
        resuspended.SuspendedById.Should().BeNull();
        resuspended.UnsuspendedById.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(DeletableRows))]
    public async Task should_record_null_actor_when_audited_base_restored_and_redeleted_anonymously(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = (IDeletableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        entity.SoftDelete();
        await db.SaveChangesAsync(AbortToken);
        _SignOut();

        // when
        entity.Undelete();
        await db.SaveChangesAsync(AbortToken);
        var restored = (IDeletableRow)await harness.ReloadAsync((IAuditedRow)entity);
        entity.SoftDelete();
        await db.SaveChangesAsync(AbortToken);
        var redeleted = (IDeletableRow)await harness.ReloadAsync((IAuditedRow)entity);

        // then
        restored.RestoredById.Should().BeNull();
        restored.DeletedById.Should().Be((UserId)"user-1");
        redeleted.DeletedById.Should().BeNull();
        redeleted.RestoredById.Should().BeNull();
    }

    [Fact]
    public async Task should_record_null_actor_and_navigation_when_navigation_base_transitions_anonymously()
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var userA = new TestAccount { Id = "user-1" };
        db.Add(userA);
        var document = new SuspendableDocument { Id = Guid.CreateVersion7(), Name = "doc" };
        db.Add(document);
        await db.SaveChangesAsync(AbortToken);
        document.Suspend(_Start, userA.Id, userA);
        await db.SaveChangesAsync(AbortToken);
        _SignOut();

        // when
        document.Unsuspend(_Start.AddMinutes(1));
        await db.SaveChangesAsync(AbortToken);
        document.Suspend(_Start.AddMinutes(2));
        await db.SaveChangesAsync(AbortToken);

        // then
        document.SuspendedBy.Should().BeNull();
        document.UnsuspendedBy.Should().BeNull();
        var saved = await harness.ReloadAsync(document);
        saved.SuspendedAt.Should().Be(_Start.AddMinutes(2));
        saved.SuspendedById.Should().BeNull();
        saved.UnsuspendedAt.Should().Be(_Start.AddMinutes(1));
        saved.UnsuspendedById.Should().BeNull();
    }

    [Fact]
    public async Task should_clear_loaded_navigation_when_navigation_base_flag_raised_anonymously()
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var userA = new TestAccount { Id = "user-1" };
        db.Add(userA);
        var document = new SuspendableDocument { Id = Guid.CreateVersion7(), Name = "doc" };
        db.Add(document);
        await db.SaveChangesAsync(AbortToken);
        document.Suspend(_Start, userA.Id, userA);
        await db.SaveChangesAsync(AbortToken);
        _SignOut();
        document.Unfreeze();
        await db.SaveChangesAsync(AbortToken);

        // when
        document.Freeze();
        await db.SaveChangesAsync(AbortToken);

        // then
        document.SuspendedById.Should().BeNull();
        document.SuspendedBy.Should().BeNull();
        var saved = await harness.ReloadAsync(document);
        saved.SuspendedById.Should().BeNull();
        saved.UnsuspendedById.Should().BeNull();
    }

    [Fact]
    public async Task should_record_null_actor_when_navigation_base_restored_and_redeleted_anonymously()
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var userA = new TestAccount { Id = "user-1" };
        db.Add(userA);
        var document = new AuditedDocument { Id = Guid.CreateVersion7(), Name = "doc" };
        db.Add(document);
        await db.SaveChangesAsync(AbortToken);
        document.Delete(_Start, userA.Id, userA);
        await db.SaveChangesAsync(AbortToken);
        _SignOut();

        // when
        document.Restore(_Start.AddMinutes(1));
        await db.SaveChangesAsync(AbortToken);
        document.Delete(_Start.AddMinutes(2));
        await db.SaveChangesAsync(AbortToken);

        // then
        document.DeletedBy.Should().BeNull();
        var saved = await harness.ReloadAsync(document);
        saved.DeletedAt.Should().Be(_Start.AddMinutes(2));
        saved.DeletedById.Should().BeNull();
        saved.RestoredAt.Should().Be(_Start.AddMinutes(1));
        saved.RestoredById.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(SuspendableRows))]
    public async Task should_return_suspended_rows_when_entity_did_not_opt_into_suspend_filter(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = (ISuspendableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        entity.Freeze();
        await db.SaveChangesAsync(AbortToken);

        // when
        await using var reader = harness.CreateContext();
        var count = await _CountAsync(reader, entity);

        // then
        count.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SuspendableRows))]
    public async Task should_hide_suspended_rows_when_entity_opted_into_suspend_filter(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<SuspendFilteredDbContext>();
        await using var db = harness.CreateContext();
        var entity = (ISuspendableRow)_Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        entity.Freeze();
        await db.SaveChangesAsync(AbortToken);

        // when
        await using var reader = harness.CreateContext();
        var filtered = await _CountAsync(reader, entity);
        var bypassed = await _CountAsync(reader, entity, ignoreSuspendFilter: true);

        // then
        filtered.Should().Be(0);
        bypassed.Should().Be(1);
    }

    [Fact]
    public void should_reject_suspend_filter_when_entity_is_not_suspendable()
    {
        // given
        EntityTypeBuilder builder = new ModelBuilder().Entity<AuditedNote>();

        // when
        var act = () => builder.HasNotSuspendedFilter();

        // then
        act.Should().Throw<ArgumentException>().WithMessage($"*{nameof(AuditedNote)}*{nameof(ISuspendAudit)}*");
    }

    [Fact]
    public async Task should_keep_transition_time_and_stamp_actor_when_navigation_base_deleted_without_actor()
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        db.Add(new TestAccount { Id = "user-1" });
        var document = new AuditedDocument { Id = Guid.CreateVersion7(), Name = "doc" };
        db.Add(document);
        await db.SaveChangesAsync(AbortToken);
        var deletedAt = _Start.AddDays(-1);
        _clock.Advance(TimeSpan.FromMinutes(5));

        // when
        document.Delete(deletedAt);
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(document);
        saved.CreatedById.Should().Be(_currentUser.UserId);
        saved.IsDeleted.Should().BeTrue();
        saved.DeletedAt.Should().Be(deletedAt);
        saved.DeletedById.Should().Be(_currentUser.UserId);
    }

    private void _SignOut()
    {
        _currentUser.UserId = null;
        _currentUser.IsAuthenticated = false;
    }

    private static Task<int> _CountAsync(DbContext db, IAuditedRow entity, bool ignoreSuspendFilter = false)
    {
        return entity switch
        {
            SuspendableNote note => _Query(db.Set<SuspendableNote>(), ignoreSuspendFilter)
                .CountAsync(x => x.Id == note.Id, AbortToken),
            SuspendableLedger ledger => _Query(db.Set<SuspendableLedger>(), ignoreSuspendFilter)
                .CountAsync(x => x.Id == ledger.Id, AbortToken),
            _ => throw new ArgumentOutOfRangeException(nameof(entity)),
        };

        static IQueryable<T> _Query<T>(IQueryable<T> query, bool ignore)
            where T : class
        {
            return ignore ? query.IgnoreNotSuspendedFilter() : query;
        }
    }

    private static IAuditedRow _Create(string kind)
    {
        return kind switch
        {
            nameof(AuditedNote) => new AuditedNote { Id = Guid.CreateVersion7(), Name = "note" },
            nameof(AuditedLedger) => new AuditedLedger { Id = Guid.CreateVersion7(), Name = "ledger" },
            nameof(SuspendableNote) => new SuspendableNote { Id = Guid.CreateVersion7(), Name = "note" },
            nameof(SuspendableLedger) => new SuspendableLedger { Id = Guid.CreateVersion7(), Name = "ledger" },
            nameof(DeletableNote) => new DeletableNote { Id = Guid.CreateVersion7(), Name = "note" },
            nameof(DeletableLedger) => new DeletableLedger { Id = Guid.CreateVersion7(), Name = "ledger" },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };
    }

    private async Task<Harness<TContext>> _CreateHarnessAsync<TContext>()
        where TContext : DbContext
    {
        var harness = new Harness<TContext>(_clock, _currentUser);

        try
        {
            await harness.InitializeAsync();

            return harness;
        }
        catch
        {
            await harness.DisposeAsync();

            throw;
        }
    }

    private sealed class Harness<TContext> : IAsyncDisposable
        where TContext : DbContext
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly ServiceProvider _provider;
        private readonly List<AsyncServiceScope> _scopes = [];

        public Harness(TimeProvider clock, ICurrentUser currentUser)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(clock);
            services.AddSingleton(currentUser);
            // The aggregate-root base raises lifecycle domain events on every save, which the pipeline refuses to drop.
            services.AddHeadlessDbContextServices().AddDomainEvents();
            services.AddDbContext<TContext>(options => options.UseSqlite(_connection).AddHeadlessExtension());
            _provider = services.BuildServiceProvider();
        }

        public async Task InitializeAsync()
        {
            await _connection.OpenAsync(AbortToken);
            await using var db = CreateContext();
            await db.Database.EnsureCreatedAsync(AbortToken);
        }

        public TContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<TContext>();
        }

        // A fresh context proves the stamped values reached the row, not just the tracked instance.
        public async Task<IAuditedRow> ReloadAsync(IAuditedRow entity)
        {
            return entity switch
            {
                AuditedNote note => await ReloadAsync(note),
                AuditedLedger ledger => await ReloadAsync(ledger),
                SuspendableNote note => await ReloadAsync(note),
                SuspendableLedger ledger => await ReloadAsync(ledger),
                DeletableNote note => await ReloadAsync(note),
                DeletableLedger ledger => await ReloadAsync(ledger),
                _ => throw new ArgumentOutOfRangeException(nameof(entity)),
            };
        }

        public async Task<TEntity> ReloadAsync<TEntity>(TEntity entity)
            where TEntity : class, IEntity<Guid>
        {
            await using var db = CreateContext();

            return await db.Set<TEntity>()
                .IgnoreNotDeletedFilter()
                .IgnoreNotSuspendedFilter()
                .SingleAsync(x => x.Id == entity.Id, AbortToken);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
            {
                await scope.DisposeAsync();
            }

            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private class AuditedDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public DbSet<AuditedNote> AuditedNotes => Set<AuditedNote>();

        public DbSet<AuditedLedger> AuditedLedgers => Set<AuditedLedger>();

        public DbSet<SuspendableNote> SuspendableNotes => Set<SuspendableNote>();

        public DbSet<SuspendableLedger> SuspendableLedgers => Set<SuspendableLedger>();

        public DbSet<DeletableNote> DeletableNotes => Set<DeletableNote>();

        public DbSet<DeletableLedger> DeletableLedgers => Set<DeletableLedger>();

        public DbSet<AuditedDocument> Documents => Set<AuditedDocument>();

        public DbSet<SuspendableDocument> SuspendableDocuments => Set<SuspendableDocument>();

        public DbSet<TestAccount> Accounts => Set<TestAccount>();

        public override string DefaultSchema => "";
    }

    // Opts both suspendable rows into the suspend filter, one through each overload.
    private sealed class SuspendFilteredDbContext(DbContextOptions<SuspendFilteredDbContext> options)
        : AuditedDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SuspendableLedger>().HasNotSuspendedFilter();
            ((EntityTypeBuilder)modelBuilder.Entity<SuspendableNote>()).HasNotSuspendedFilter();
        }
    }

    // Behavior the tests drive through each base's protected setters, as a consuming entity would.
    public interface IAuditedRow : IEntity<Guid>, ICreateAudit<UserId>, IUpdateAudit<UserId>
    {
        void Rename(string name);
    }

    public interface ISuspendableRow : IAuditedRow, ISuspendAudit<UserId>
    {
        void Freeze();

        void Unfreeze();
    }

    public interface IDeletableRow : IAuditedRow, IDeleteAudit<UserId>
    {
        void SoftDelete();

        void Undelete();
    }

    public sealed class AuditedNote : AuditedEntity<Guid, UserId>, IAuditedRow
    {
        public required string Name { get; set; }

        public void Rename(string name) => Name = name;
    }

    public sealed class AuditedLedger : AuditedAggregateRoot<Guid, UserId>, IAuditedRow
    {
        public required string Name { get; set; }

        public void Rename(string name) => Name = name;
    }

    public sealed class SuspendableNote : SuspendableEntity<Guid, UserId>, ISuspendableRow
    {
        public required string Name { get; set; }

        public void Rename(string name) => Name = name;

        public void Freeze() => IsSuspended = true;

        public void Unfreeze() => IsSuspended = false;
    }

    public sealed class SuspendableLedger : SuspendableAggregateRoot<Guid, UserId>, ISuspendableRow
    {
        public required string Name { get; set; }

        public void Rename(string name) => Name = name;

        public void Freeze() => IsSuspended = true;

        public void Unfreeze() => IsSuspended = false;
    }

    public sealed class DeletableNote : SoftDeletableEntity<Guid, UserId>, IDeletableRow
    {
        public required string Name { get; set; }

        public void Rename(string name) => Name = name;

        public void SoftDelete() => IsDeleted = true;

        public void Undelete() => IsDeleted = false;
    }

    public sealed class DeletableLedger : SoftDeletableAggregateRoot<Guid, UserId>, IDeletableRow
    {
        public required string Name { get; set; }

        public void Rename(string name) => Name = name;

        public void SoftDelete() => IsDeleted = true;

        public void Undelete() => IsDeleted = false;
    }

    public sealed class AuditedDocument : SoftDeletableAggregateRoot<Guid, UserId, TestAccount>
    {
        public required string Name { get; set; }
    }

    public sealed class SuspendableDocument : SuspendableAggregateRoot<Guid, UserId, TestAccount>
    {
        public required string Name { get; set; }

        public void Freeze() => IsSuspended = true;

        public void Unfreeze() => IsSuspended = false;
    }

    public sealed class TestAccount
    {
        public required UserId Id { get; init; }
    }
}
