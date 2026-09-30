// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Primitives;
using Headless.Testing.Helpers;
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

    public static TheoryData<string> Bases => [nameof(AuditedNote), nameof(AuditedLedger)];

    [Theory]
    [MemberData(nameof(Bases))]
    public async Task should_stamp_create_audit_when_audited_base_added(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync();
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
    [MemberData(nameof(Bases))]
    public async Task should_stamp_update_audit_when_audited_base_modified(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync();
        await using var db = harness.CreateContext();
        var entity = _Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _currentUser.UserId = "user-2";

        // when
        entity.Rename("renamed");
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(entity);
        saved.CreatedAt.Should().Be(_Start);
        saved.CreatedById.Should().Be((UserId)"user-1");
        saved.UpdatedAt.Should().Be(_Start.AddMinutes(5));
        saved.UpdatedById.Should().Be((UserId)"user-2");
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public async Task should_stamp_delete_audit_when_audited_base_soft_deleted(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync();
        await using var db = harness.CreateContext();
        var entity = _Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));

        // when
        entity.SoftDelete();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(entity);
        saved.IsDeleted.Should().BeTrue();
        saved.DeletedAt.Should().Be(_Start.AddMinutes(5));
        saved.DeletedById.Should().Be(_currentUser.UserId);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public async Task should_clear_delete_audit_when_audited_base_restored(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync();
        await using var db = harness.CreateContext();
        var entity = _Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        entity.SoftDelete();
        await db.SaveChangesAsync(AbortToken);

        // when
        entity.Undelete();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(entity);
        saved.IsDeleted.Should().BeFalse();
        saved.DeletedAt.Should().BeNull();
        saved.DeletedById.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public async Task should_stamp_suspend_audit_when_audited_base_suspended(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync();
        await using var db = harness.CreateContext();
        var entity = _Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(5));

        // when
        entity.Freeze();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(entity);
        saved.IsSuspended.Should().BeTrue();
        saved.SuspendedAt.Should().Be(_Start.AddMinutes(5));
        saved.SuspendedById.Should().Be(_currentUser.UserId);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public async Task should_clear_suspend_audit_when_audited_base_unsuspended(string kind)
    {
        // given
        await using var harness = await _CreateHarnessAsync();
        await using var db = harness.CreateContext();
        var entity = _Create(kind);
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        entity.Freeze();
        await db.SaveChangesAsync(AbortToken);

        // when
        entity.Unfreeze();
        await db.SaveChangesAsync(AbortToken);

        // then
        var saved = await harness.ReloadAsync(entity);
        saved.IsSuspended.Should().BeFalse();
        saved.SuspendedAt.Should().BeNull();
        saved.SuspendedById.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_transition_time_and_stamp_actor_when_navigation_base_deleted_without_actor()
    {
        // given
        await using var harness = await _CreateHarnessAsync();
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
        var saved = await harness.ReloadAsync<AuditedDocument>(document);
        saved.CreatedById.Should().Be(_currentUser.UserId);
        saved.IsDeleted.Should().BeTrue();
        saved.DeletedAt.Should().Be(deletedAt);
        saved.DeletedById.Should().Be(_currentUser.UserId);
    }

    private static IAuditedRow _Create(string kind)
    {
        return kind switch
        {
            nameof(AuditedNote) => new AuditedNote { Id = Guid.CreateVersion7(), Name = "note" },
            nameof(AuditedLedger) => new AuditedLedger { Id = Guid.CreateVersion7(), Name = "ledger" },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };
    }

    private async Task<Harness> _CreateHarnessAsync()
    {
        var harness = new Harness(_clock, _currentUser);

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

    private sealed class Harness : IAsyncDisposable
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
            services.AddDbContext<AuditedDbContext>(options => options.UseSqlite(_connection).AddHeadlessExtension());
            _provider = services.BuildServiceProvider();
        }

        public async Task InitializeAsync()
        {
            await _connection.OpenAsync(AbortToken);
            await using var db = CreateContext();
            await db.Database.EnsureCreatedAsync(AbortToken);
        }

        public AuditedDbContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<AuditedDbContext>();
        }

        // A fresh context proves the stamped values reached the row, not just the tracked instance.
        public async Task<IAuditedRow> ReloadAsync(IAuditedRow entity)
        {
            return entity switch
            {
                AuditedNote note => await ReloadAsync(note),
                AuditedLedger ledger => await ReloadAsync(ledger),
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

    private sealed class AuditedDbContext(HeadlessDbContextServices services, DbContextOptions options)
        : HeadlessDbContext(services, options)
    {
        public DbSet<AuditedNote> Notes => Set<AuditedNote>();

        public DbSet<AuditedLedger> Ledgers => Set<AuditedLedger>();

        public DbSet<AuditedDocument> Documents => Set<AuditedDocument>();

        public DbSet<TestAccount> Accounts => Set<TestAccount>();

        public override string DefaultSchema => "";
    }

    // Behavior the tests drive through each base's protected setters, as a consuming entity would.
    public interface IAuditedRow
        : IEntity<Guid>,
            ICreateAudit<UserId>,
            IUpdateAudit<UserId>,
            IDeleteAudit<UserId>,
            ISuspendAudit<UserId>
    {
        void Rename(string name);

        void SoftDelete();

        void Undelete();

        void Freeze();

        void Unfreeze();
    }

    public sealed class AuditedNote : AuditedEntity<Guid, UserId>, IAuditedRow
    {
        public required string Name { get; set; }

        public void Rename(string name) => Name = name;

        public void SoftDelete() => IsDeleted = true;

        public void Undelete() => IsDeleted = false;

        public void Freeze() => IsSuspended = true;

        public void Unfreeze() => IsSuspended = false;
    }

    public sealed class AuditedLedger : AuditedAggregateRoot<Guid, UserId>, IAuditedRow
    {
        public required string Name { get; set; }

        public void Rename(string name) => Name = name;

        public void SoftDelete() => IsDeleted = true;

        public void Undelete() => IsDeleted = false;

        public void Freeze() => IsSuspended = true;

        public void Unfreeze() => IsSuspended = false;
    }

    public sealed class AuditedDocument : AuditedAggregateRoot<Guid, UserId, TestAccount>
    {
        public required string Name { get; set; }
    }

    public sealed class TestAccount
    {
        public required UserId Id { get; init; }
    }
}
