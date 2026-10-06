// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Primitives;
using Headless.Testing;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        [nameof(AuditedNote), nameof(AuditedLedger), nameof(DeletableNote), nameof(DeletableLedger)];

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
    [MemberData(nameof(Rows))]
    public async Task should_record_null_updater_when_audited_base_modified_anonymously(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var entity = _Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        entity.Rename("renamed by user-1");
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _SignOut();

        // when
        entity.Rename("renamed anonymously");
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(entity);
        saved.UpdatedAt.Should().Be(_Start.AddMinutes(5));
        saved.UpdatedById.Should().BeNull();
    }

    [Fact]
    public async Task should_clear_loaded_updater_navigation_when_navigation_base_modified_anonymously()
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        var userA = new TestAccount { Id = "user-1" };
        db.Add(userA);
        var document = new AuditedDocument { Id = Guid.CreateVersion7(), Name = "doc" };
        db.Add(document);
        await db.SaveChangesAsync(AbortToken);
        document.Update(_Start, userA.Id, userA);
        await db.SaveChangesAsync(AbortToken);
        _SignOut();

        // when
        document.Name = "renamed anonymously";
        await db.SaveChangesAsync(AbortToken);

        // then
        document.UpdatedById.Should().BeNull();
        document.UpdatedBy.Should().BeNull();
        var saved = await harness.ReloadAsync(document);
        saved.UpdatedById.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_explicit_updater_when_update_called_anonymously()
    {
        // given
        await using var harness = await _CreateHarnessAsync<AuditedDbContext>();
        await using var db = harness.CreateContext();
        db.Add(new TestAccount { Id = "user-1" });
        var owner = new TestAccount { Id = "owner" };
        db.Add(owner);
        var document = new AuditedDocument { Id = Guid.CreateVersion7(), Name = "doc" };
        db.Add(document);
        await db.SaveChangesAsync(AbortToken);
        _SignOut();
        document.Update(_Start.AddMinutes(1), owner.Id, owner);
        await db.SaveChangesAsync(AbortToken);

        // when: the same actor updates again, so the id value itself does not change in this save
        document.Name = "renamed";
        document.Update(_Start.AddMinutes(2), owner.Id, owner);
        await db.SaveChangesAsync(AbortToken);

        // then
        document.UpdatedBy.Should().BeSameAs(owner);
        var saved = await harness.ReloadAsync(document);
        saved.UpdatedAt.Should().Be(_Start.AddMinutes(2));
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

    private static IAuditedRow _Create(string kind)
    {
        return kind switch
        {
            nameof(AuditedNote) => new AuditedNote { Id = Guid.CreateVersion7(), Name = "note" },
            nameof(AuditedLedger) => new AuditedLedger { Id = Guid.CreateVersion7(), Name = "ledger" },
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
            services.AddDbContext<TContext>(options => options.UseSqlite(_connection));
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
                DeletableNote note => await ReloadAsync(note),
                DeletableLedger ledger => await ReloadAsync(ledger),
                _ => throw new ArgumentOutOfRangeException(nameof(entity)),
            };
        }

        public async Task<TEntity> ReloadAsync<TEntity>(TEntity entity)
            where TEntity : class, IEntity<Guid>
        {
            await using var db = CreateContext();

            return await db.Set<TEntity>().IgnoreNotDeletedFilter().SingleAsync(x => x.Id == entity.Id, AbortToken);
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

    private sealed class AuditedDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public DbSet<AuditedNote> AuditedNotes => Set<AuditedNote>();

        public DbSet<AuditedLedger> AuditedLedgers => Set<AuditedLedger>();

        public DbSet<DeletableNote> DeletableNotes => Set<DeletableNote>();

        public DbSet<DeletableLedger> DeletableLedgers => Set<DeletableLedger>();

        public DbSet<AuditedDocument> Documents => Set<AuditedDocument>();

        public DbSet<TestAccount> Accounts => Set<TestAccount>();

        public override string DefaultSchema => "";
    }

    // Behavior the tests drive through each base's protected setters, as a consuming entity would.
    public interface IAuditedRow : IEntity<Guid>, ICreateAudit<UserId>, IUpdateAudit<UserId>
    {
        void Rename(string name);
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

    public sealed class TestAccount
    {
        public required UserId Id { get; init; }
    }
}
