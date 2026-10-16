// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>The per-entity and per-query work a save or a tenant-filtered query repeats.</summary>
public sealed class PerEntityCostTests : TestBase
{
    [Fact]
    public async Task should_read_the_current_user_once_per_save_when_many_audited_entities_are_added()
    {
        // given
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((UserId)"user-1");
        await using var harness = await Harness.CreateAsync(services => services.AddSingleton(user));
        var db = harness.CreateContext();
        db.AddRange(Enumerable.Range(0, 100).Select(_ => new Note { Id = Guid.CreateVersion7() }));
        user.ClearReceivedCalls();

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        _Reads(user, nameof(ICurrentUser.UserId)).Should().Be(1);
        _Reads(user, nameof(ICurrentUser.AccountId)).Should().Be(1);
        db.Set<Note>().Local.Should().OnlyContain(n => n.CreatedById == (UserId)"user-1");
    }

    [Fact]
    public async Task should_read_the_current_user_once_per_save_when_every_creator_is_already_set()
    {
        // given
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((UserId)"user-1");
        await using var harness = await Harness.CreateAsync(services => services.AddSingleton(user));
        var db = harness.CreateContext();
        db.AddRange(Enumerable.Range(0, 10).Select(_ => Note.CreatedBy("someone")));
        user.ClearReceivedCalls();

        // when
        await db.SaveChangesAsync(AbortToken);

        // then: only the audit capture reads the actor, once for the save
        _Reads(user, nameof(ICurrentUser.UserId)).Should().Be(1);
        _Reads(user, nameof(ICurrentUser.AccountId)).Should().Be(1);
    }

    [Fact]
    public async Task should_read_the_ambient_tenant_once_per_query_over_a_required_tenant_column()
    {
        // given
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("tenant-1");
        await using var harness = await Harness.CreateAsync(services => services.AddSingleton(tenant));
        var seed = harness.CreateContext();
        seed.Add(new Ledger { Id = Guid.CreateVersion7() });
        seed.Entry(seed.Set<Ledger>().Local.Single()).Property("TenantId").CurrentValue = "tenant-1";
        await seed.SaveChangesAsync(AbortToken);
        var db = harness.CreateContext();
        tenant.ClearReceivedCalls();

        // when
        var rows = await db.Set<Ledger>().ToListAsync(AbortToken);

        // then
        rows.Should().ContainSingle();
        _Reads(tenant, nameof(ICurrentTenant.Id)).Should().Be(1);
    }

    [Fact]
    public async Task should_return_no_rows_over_a_required_tenant_column_when_no_tenant_is_set()
    {
        // given
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("tenant-1");
        await using var harness = await Harness.CreateAsync(services => services.AddSingleton(tenant));
        var seed = harness.CreateContext();
        seed.Add(new Ledger { Id = Guid.CreateVersion7() });
        seed.Entry(seed.Set<Ledger>().Local.Single()).Property("TenantId").CurrentValue = "tenant-1";
        await seed.SaveChangesAsync(AbortToken);
        tenant.Id.Returns((string?)null);

        // when
        var rows = await harness.CreateContext().Set<Ledger>().ToListAsync(AbortToken);

        // then
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task should_run_processors_once_per_entity_when_a_handler_adds_an_entity_during_the_save()
    {
        // given
        var counter = new ProcessCounter();
        await using var harness = await Harness.CreateAsync(
            services =>
            {
                services.AddSingleton(counter);
                services.AddScoped<IDomainEventHandler<NoteFiled>, NoteFiledHandler>();
            },
            headless => headless.AddSaveEntryProcessor<ProcessCounter>(ServiceLifetime.Singleton),
            addDomainEvents: true
        );
        var db = harness.CreateContext();
        var root = new Folder { Id = Guid.CreateVersion7() };
        root.File();
        db.Add(root);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then: the folder is processed in the first pass, the note the handler added in the recollection only
        counter.Calls.Should().BeEquivalentTo(new Dictionary<Type, int> { [typeof(Folder)] = 1, [typeof(Note)] = 1 });
    }

    [Fact]
    public async Task should_resolve_the_owners_of_owned_tenant_entries_in_linear_time()
    {
        // given
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("tenant-1");
        await using var harness = await Harness.CreateAsync(services =>
        {
            services.AddSingleton(tenant);
            services.PostConfigure<TenantGuardOptions>(options => options.GuardWrites = true);
        });

        // when
        var small = _AllocatedSavingOwnedLedgers(harness, 100);
        var large = _AllocatedSavingOwnedLedgers(harness, 400);

        // then: four times the rows cost about four times the bytes, where a tracker scan per owned entry costs sixteen
        ((double)large / small)
            .Should()
            .BeLessThan(6);
    }

    private static long _AllocatedSavingOwnedLedgers(Harness harness, int count)
    {
        var db = harness.CreateContext();
        db.AddRange(
            Enumerable
                .Range(0, count)
                .Select(_ => new Ledger
                {
                    Id = Guid.CreateVersion7(),
                    Totals = new LedgerTotals { Balance = 1 },
                })
        );
        var before = GC.GetAllocatedBytesForCurrentThread();
        db.SaveChanges();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static int _Reads(object substitute, string property) =>
        substitute
            .ReceivedCalls()
            .Count(call => string.Equals(call.GetMethodInfo().Name, "get_" + property, StringComparison.Ordinal));

    public sealed record NoteFiled(Guid FolderId);

    public sealed class Note : Entity<Guid>, ICreateAudit<UserId>
    {
        public DateTimeOffset CreatedAt { get; }

        public UserId? CreatedById { get; private set; }

        public static Note CreatedBy(string userId) => new() { Id = Guid.CreateVersion7(), CreatedById = userId };
    }

    public sealed class Folder : AggregateRoot<Guid>
    {
        public void File() => AddDomainEvent(new NoteFiled(Id));
    }

    /// <summary>A root whose tenant column is required because the type does not implement IMultiTenant.</summary>
    public sealed class Ledger : Entity<Guid>
    {
        public LedgerTotals? Totals { get; set; }
    }

    public sealed class LedgerTotals
    {
        public decimal Balance { get; set; }
    }

    private sealed class NoteFiledHandler(LedgerDbContext db) : IDomainEventHandler<NoteFiled>
    {
        public ValueTask HandleAsync(EventContext<NoteFiled> context, CancellationToken cancellationToken = default)
        {
            db.Add(new Note { Id = Guid.CreateVersion7() });

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProcessCounter : IHeadlessSaveEntryProcessor
    {
        public Dictionary<Type, int> Calls { get; } = [];

        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry.State is EntityState.Added)
            {
                Calls[entry.Entity.GetType()] = Calls.GetValueOrDefault(entry.Entity.GetType()) + 1;
            }
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly List<AsyncServiceScope> _scopes = [];
        private ServiceProvider _provider = null!;

        public static async Task<Harness> CreateAsync(
            Action<IServiceCollection> configure,
            Action<HeadlessDbContextOptions>? headless = null,
            bool addDomainEvents = false
        )
        {
            var harness = new Harness();
            await harness._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();
            configure(services);
            var builder = services.AddHeadlessDbContext<LedgerDbContext>(
                options => options.UseSqlite(harness._connection),
                headless
            );

            if (addDomainEvents)
            {
                builder.AddDomainEvents();
            }

            harness._provider = services.BuildServiceProvider();
            await harness.CreateContext().Database.EnsureCreatedAsync(AbortToken);

            return harness;
        }

        public LedgerDbContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
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

    public sealed class LedgerDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();

        public DbSet<Folder> Folders => Set<Folder>();

        public DbSet<Ledger> Ledgers => Set<Ledger>();

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Ledger>().IsTenantOwned().OwnsOne(x => x.Totals);
        }
    }
}
