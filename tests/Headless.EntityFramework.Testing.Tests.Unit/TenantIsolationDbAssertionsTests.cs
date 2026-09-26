// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.EntityFramework.Testing;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class TenantIsolationDbAssertionsTests : TestBase
{
    private readonly List<NotesContext> _created = [];
    private readonly List<SqliteConnection> _connections = [];
    private readonly List<ServiceProvider> _providers = [];

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_pass_for_a_tenant_owned_entity_with_the_write_guard_on()
    {
        var (world, create) = await _WorldAsync(guardWrites: true);
        var id = await _SeedOwnedAsync(world, create);

        await TenantIsolationDbAssertions.ShouldNotSeeAcrossTenantsAsync<OwnedNote>(world, create, id, AbortToken);
    }

    [Fact]
    public async Task should_apply_the_callers_mutation_to_the_guarded_update()
    {
        var (world, create) = await _WorldAsync(guardWrites: true);
        var id = await _SeedOwnedAsync(world, create);
        var mutated = 0;

        await TenantIsolationDbAssertions.ShouldRefuseWritesAcrossTenantsAsync<OwnedNote>(
            world,
            create,
            id,
            note =>
            {
                note.Text = "compromised";
                mutated++;
            },
            AbortToken
        );

        mutated.Should().Be(1);
        await _ShouldStillReadAsync(world, create, id, "original");
    }

    [Fact]
    public async Task should_fail_the_read_check_when_tenant_b_sees_the_row()
    {
        var (world, create) = await _WorldAsync(guardWrites: true);
        var id = await _SeedSharedAsync(world, create);

        var act = () =>
            TenantIsolationDbAssertions.ShouldNotReadAcrossTenantsAsync<SharedNote>(world, create, id, AbortToken);

        (await act.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("tenant-b read tenant-a's SharedNote")
            .And.Contain("tenant query filter");
    }

    [Fact]
    public async Task should_fail_the_write_check_when_the_write_guard_is_off()
    {
        var (world, create) = await _WorldAsync(guardWrites: false);
        var id = await _SeedOwnedAsync(world, create);

        var act = () =>
            TenantIsolationDbAssertions.ShouldRefuseWritesAcrossTenantsAsync<OwnedNote>(
                world,
                create,
                id,
                cancellationToken: AbortToken
            );

        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("GuardTenantWrites()");
    }

    [Fact]
    public async Task should_fail_every_assertion_when_the_owner_cannot_see_the_key()
    {
        var (world, create) = await _WorldAsync(guardWrites: true);
        var missing = Guid.NewGuid();

        Func<Task>[] assertions =
        [
            () =>
                TenantIsolationDbAssertions.ShouldNotReadAcrossTenantsAsync<OwnedNote>(
                    world,
                    create,
                    missing,
                    AbortToken
                ),
            () =>
                TenantIsolationDbAssertions.ShouldRefuseWritesAcrossTenantsAsync<OwnedNote>(
                    world,
                    create,
                    missing,
                    cancellationToken: AbortToken
                ),
            () =>
                TenantIsolationDbAssertions.ShouldNotSeeAcrossTenantsAsync<OwnedNote>(
                    world,
                    create,
                    missing,
                    AbortToken
                ),
        ];

        foreach (var assertion in assertions)
        {
            (await assertion.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("proves nothing");
        }
    }

    [Fact]
    public async Task should_reject_a_key_of_the_wrong_type()
    {
        var (world, create) = await _WorldAsync(guardWrites: true);
        await _SeedOwnedAsync(world, create);

        var act = () =>
            TenantIsolationDbAssertions.ShouldNotReadAcrossTenantsAsync<OwnedNote>(
                world,
                create,
                "not-a-guid",
                AbortToken
            );

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(nameof(Guid));
    }

    [Fact]
    public async Task should_reject_an_entity_with_a_composite_key()
    {
        var (world, create) = await _WorldAsync(guardWrites: true);

        var act = () =>
            TenantIsolationDbAssertions.ShouldNotReadAcrossTenantsAsync<CompositeNote>(world, create, 1, AbortToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("composite");
    }

    [Fact]
    public async Task should_pass_when_the_row_is_physically_out_of_tenant_bs_reach()
    {
        // Tenant B's context points at a separate database, as a database-per-tenant placement would.
        var (world, owner) = await _WorldAsync(guardWrites: true);
        var id = await _SeedOwnedAsync(world, owner);
        var (_, other) = await _WorldAsync(guardWrites: true, world.CurrentTenant);

        NotesContext placed() =>
            string.Equals(world.CurrentTenant.Id, world.TenantB, StringComparison.Ordinal) ? other() : owner();

        await TenantIsolationDbAssertions.ShouldNotSeeAcrossTenantsAsync<OwnedNote>(world, placed, id, AbortToken);
    }

    [Fact]
    public async Task should_dispose_every_context_it_creates()
    {
        var (world, create) = await _WorldAsync(guardWrites: true);
        var id = await _SeedOwnedAsync(world, create);
        _created.Clear();

        await TenantIsolationDbAssertions.ShouldNotSeeAcrossTenantsAsync<OwnedNote>(world, create, id, AbortToken);

        _created.Should().NotBeEmpty();
        foreach (var context in _created)
        {
            var use = () => context.Set<OwnedNote>().Count();
            use.Should().Throw<ObjectDisposedException>();
        }
    }

    private async Task<(TenantWorld World, Func<NotesContext> Create)> _WorldAsync(
        bool guardWrites,
        ICurrentTenant? currentTenant = null
    )
    {
        var world = new TenantWorld(currentTenant ?? new TestCurrentTenant());
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        _connections.Add(connection);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHeadlessDbContextServices();

        if (guardWrites)
        {
            builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
        }

        builder.Services.AddSingleton(world.CurrentTenant);
        var provider = builder.Services.BuildServiceProvider();
        _providers.Add(provider);

        var options = new DbContextOptionsBuilder<NotesContext>().UseSqlite(connection).AddHeadlessExtension().Options;

        NotesContext create()
        {
            var context = new NotesContext(provider.GetRequiredService<HeadlessDbContextServices>(), options);
            _created.Add(context);

            return context;
        }

        await using (var db = create())
        {
            await db.Database.EnsureCreatedAsync(AbortToken);
        }

        return (world, create);
    }

    private async Task<Guid> _SeedOwnedAsync(TenantWorld world, Func<NotesContext> create)
    {
        using var _ = world.AsTenantA();
        await using var db = create();
        // Set explicitly: without the tenancy registration nothing stamps the owner on add.
        var note = new OwnedNote { Owner = world.TenantA };
        db.Add(note);
        await db.SaveChangesAsync(AbortToken);

        return note.Id;
    }

    private async Task<Guid> _SeedSharedAsync(TenantWorld world, Func<NotesContext> create)
    {
        using var _ = world.AsTenantA();
        await using var db = create();
        var note = new SharedNote();
        db.Add(note);
        await db.SaveChangesAsync(AbortToken);

        return note.Id;
    }

    private async Task _ShouldStillReadAsync(TenantWorld world, Func<NotesContext> create, Guid id, string text)
    {
        using var _ = world.AsTenantA();
        await using var db = create();
        (await db.Set<OwnedNote>().SingleAsync(x => x.Id == id, AbortToken)).Text.Should().Be(text);
    }
}

public sealed class NotesContext(HeadlessDbContextServices services, DbContextOptions<NotesContext> options)
    : HeadlessDbContext(services, options)
{
    public override string? DefaultSchema => null;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OwnedNote>().IsTenantOwned(nameof(OwnedNote.Owner));
        modelBuilder.Entity<SharedNote>();
        var composite = modelBuilder.Entity<CompositeNote>();
        composite.HasKey(x => new { x.Id, x.Part });
        composite.IsTenantOwned(nameof(CompositeNote.Owner));
    }
}

public sealed class OwnedNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Owner { get; set; }
    public string Text { get; set; } = "original";
}

public sealed class SharedNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "original";
}

public sealed class CompositeNote
{
    public int Id { get; set; }
    public int Part { get; set; }
    public string? Owner { get; set; }
}
