// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Primitives;
using Headless.Testing;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class StampVisibilityTests : TestBase
{
    [Fact]
    public async Task should_flag_new_concurrency_stamp_modified_before_change_detection_when_entity_updated()
    {
        // given
        await using var harness = await Harness.CreateAsync();
        await using var db = harness.CreateContext();
        var row = new StampedRow { Id = Guid.CreateVersion7(), Name = "a" };
        db.Add(row);
        await db.SaveChangesAsync(AbortToken);
        var originalStamp = row.ConcurrencyStamp;
        harness.Observer.Clear();

        // when
        row.Name = "b";
        await db.SaveChangesAsync(AbortToken);

        // then: a later processor already sees the stamp as a pending change with the old value as its original
        var seen = harness.Observer.Seen.Should().ContainSingle().Subject;
        seen.StampModified.Should().BeTrue();
        seen.StampOriginal.Should().Be(originalStamp);
        seen.StampCurrent.Should().NotBe(originalStamp).And.Be(row.ConcurrencyStamp);
    }

    [Fact]
    public async Task should_store_entity_stamp_and_reject_stale_stamp_when_entity_updated()
    {
        // given
        await using var harness = await Harness.CreateAsync();
        await using var writer = harness.CreateContext();
        var row = new StampedRow { Id = Guid.CreateVersion7(), Name = "a" };
        writer.Add(row);
        await writer.SaveChangesAsync(AbortToken);
        await using var stale = harness.CreateContext();
        var staleRow = await stale.Set<StampedRow>().SingleAsync(x => x.Id == row.Id, AbortToken);

        // when
        row.Name = "b";
        await writer.SaveChangesAsync(AbortToken);
        staleRow.Name = "c";
        var act = () => stale.SaveChangesAsync(AbortToken);

        // then
        await using var reader = harness.CreateContext();
        var stored = await reader.Set<StampedRow>().AsNoTracking().SingleAsync(x => x.Id == row.Id, AbortToken);
        stored.ConcurrencyStamp.Should().Be(row.ConcurrencyStamp);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task should_flag_create_audit_and_keep_explicit_creator_when_entity_added()
    {
        // given
        await using var harness = await Harness.CreateAsync();
        await using var db = harness.CreateContext();
        var stamped = new StampedRow { Id = Guid.CreateVersion7(), Name = "a" };
        var explicitCreator = new StampedRow { Id = Guid.CreateVersion7(), Name = "b" };
        explicitCreator.SetCreator("someone-else");
        db.AddRange(stamped, explicitCreator);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        stamped.CreatedAt.Should().Be(Harness.Now);
        stamped.CreatedById.Should().Be((UserId)"user-1");
        explicitCreator.CreatedById.Should().Be((UserId)"someone-else");
        harness.Observer.Seen.Should().OnlyContain(x => x.CreatedAtSet);
    }

    [Fact]
    public async Task should_stamp_ambient_tenant_on_unguarded_tenant_owned_add()
    {
        // given
        await using var harness = await Harness.CreateAsync();
        await using var db = harness.CreateContext();
        var row = new TenantRow { Id = Guid.CreateVersion7() };
        using var _ = harness.ChangeTenant("tenant-1");
        db.Add(row);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        row.TenantId.Should().Be("tenant-1");
    }

    private sealed class StampObserver : IHeadlessSaveEntryProcessor
    {
        public List<Observation> Seen { get; } = [];

        public void Clear() => Seen.Clear();

        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is not { Entity: StampedRow, State: EntityState.Added or EntityState.Modified })
            {
                return;
            }

            var stamp = entry.Property(nameof(StampedRow.ConcurrencyStamp));
            var createdAt = entry.Property(nameof(StampedRow.CreatedAt));
            Seen.Add(
                new(
                    stamp.IsModified,
                    (string?)stamp.OriginalValue,
                    (string?)stamp.CurrentValue,
                    (DateTimeOffset)createdAt.CurrentValue! != default
                )
            );
        }
    }

    private sealed record Observation(
        bool StampModified,
        string? StampOriginal,
        string? StampCurrent,
        bool CreatedAtSet
    );

    private sealed class Harness : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly List<AsyncServiceScope> _scopes = [];
        private ServiceProvider _provider = null!;

        public StampObserver Observer { get; } = new();

        public static async Task<Harness> CreateAsync()
        {
            var harness = new Harness();
            await harness._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
            services.AddSingleton<ICurrentUser>(new TestCurrentUser { UserId = "user-1", IsAuthenticated = true });
            services.AddSingleton(harness.Observer);
            services.AddHeadlessDbContext<StampDbContext>(
                options => options.UseSqlite(harness._connection),
                headless => headless.AddSaveEntryProcessor<StampObserver>(ServiceLifetime.Singleton)
            );
            harness._provider = services.BuildServiceProvider();
            await using var db = harness.CreateContext();
            await db.Database.EnsureCreatedAsync(AbortToken);

            return harness;
        }

        public StampDbContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<StampDbContext>();
        }

        public IDisposable ChangeTenant(string tenantId) =>
            _provider.GetRequiredService<Headless.MultiTenancy.ICurrentTenant>().Change(tenantId);

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

    public sealed class StampedRow : Entity<Guid>, IHasConcurrencyStamp, ICreateAudit<UserId>
    {
        public string Name { get; set; } = "";

        public string? ConcurrencyStamp { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public UserId? CreatedById { get; private set; }

        public void SetCreator(string userId) => CreatedById = userId;
    }

    public sealed class TenantRow : Entity<Guid>, IMultiTenant
    {
        public string? TenantId { get; private set; }
    }

    public sealed class StampDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public DbSet<StampedRow> Stamped => Set<StampedRow>();

        public DbSet<TenantRow> Tenants => Set<TenantRow>();

        public override string DefaultSchema => "";
    }
}
