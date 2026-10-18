// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Writes made to plain CLR properties during <c>SaveChanges</c> (by a <c>SavingChanges</c> handler, a save
/// interceptor, or a domain-event handler) are found by EF Core's own change detection inside the base save, and so
/// are stored. They pin that the pipeline keeps that detection pass.
/// </summary>
public sealed class SaveChangesWriteVisibilityTests : TestBase
{
    [Fact]
    public async Task should_store_a_property_set_by_a_saving_changes_handler()
    {
        // given
        await using var harness = await Harness.CreateAsync();
        var db = harness.CreateContext();
        var row = new Ticket { Id = Guid.CreateVersion7(), Title = "a" };
        db.Add(row);
        await db.SaveChangesAsync(AbortToken);
        db.SavingChanges += (_, _) => row.Note = "set while saving";

        // when
        row.Title = "b";
        await db.SaveChangesAsync(AbortToken);

        // then
        (await harness.ReloadAsync(row.Id))
            .Note.Should()
            .Be("set while saving");
    }

    [Fact]
    public async Task should_store_a_property_set_by_a_save_interceptor()
    {
        // given
        await using var harness = await Harness.CreateAsync(services =>
            services.AddSingleton<IInterceptor>(new NoteWritingInterceptor())
        );
        var db = harness.CreateContext();
        var row = new Ticket { Id = Guid.CreateVersion7(), Title = "a" };
        db.Add(row);
        await db.SaveChangesAsync(AbortToken);

        // when
        row.Title = "b";
        await db.SaveChangesAsync(AbortToken);

        // then
        (await harness.ReloadAsync(row.Id))
            .Note.Should()
            .Be("set by interceptor");
    }

    [Fact]
    public async Task should_store_a_property_a_domain_event_handler_sets_on_another_tracked_entity()
    {
        // given
        await using var harness = await Harness.CreateAsync(services =>
            services.AddScoped<IDomainEventHandler<TicketClosed>, TicketClosedHandler>()
        );
        var db = harness.CreateContext();
        var other = new Ticket { Id = Guid.CreateVersion7(), Title = "other" };
        var board = new Board { Id = Guid.CreateVersion7() };
        db.AddRange(other, board);
        await db.SaveChangesAsync(AbortToken);

        // when
        board.Close(other.Id);
        await db.SaveChangesAsync(AbortToken);

        // then
        (await harness.ReloadAsync(other.Id))
            .Note.Should()
            .Be("closed by board");
    }

    public sealed record TicketClosed(Guid TicketId);

    public sealed class Ticket : Entity<Guid>
    {
        public string Title { get; set; } = "";

        public string? Note { get; set; }
    }

    public sealed class Board : AggregateRoot<Guid>
    {
        public int Closed { get; private set; }

        public void Close(Guid ticketId)
        {
            Closed++;
            AddDomainEvent(new TicketClosed(ticketId));
        }
    }

    private sealed class TicketClosedHandler(TicketDbContext db) : IDomainEventHandler<TicketClosed>
    {
        public ValueTask HandleAsync(EventContext<TicketClosed> context, CancellationToken cancellationToken = default)
        {
            db.Set<Ticket>().Local.Single(t => t.Id == context.Payload.TicketId).Note = "closed by board";

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoteWritingInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            foreach (var ticket in eventData.Context!.ChangeTracker.Entries<Ticket>().Select(e => e.Entity))
            {
                ticket.Note = "set by interceptor";
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly List<AsyncServiceScope> _scopes = [];
        private ServiceProvider _provider = null!;

        public static async Task<Harness> CreateAsync(Action<IServiceCollection>? configure = null)
        {
            var harness = new Harness();
            await harness._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();
            configure?.Invoke(services);
            services
                .AddHeadlessDbContext<TicketDbContext>(options => options.UseSqlite(harness._connection))
                .AddDomainEvents();
            harness._provider = services.BuildServiceProvider();
            await harness.CreateContext().Database.EnsureCreatedAsync(AbortToken);

            return harness;
        }

        public TicketDbContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        }

        public Task<Ticket> ReloadAsync(Guid id) =>
            CreateContext().Set<Ticket>().AsNoTracking().SingleAsync(t => t.Id == id, AbortToken);

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

    public sealed class TicketDbContext(DbContextOptions<TicketDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<Ticket> Tickets => Set<Ticket>();

        public DbSet<Board> Boards => Set<Board>();

        public override string DefaultSchema => "";
    }
}
