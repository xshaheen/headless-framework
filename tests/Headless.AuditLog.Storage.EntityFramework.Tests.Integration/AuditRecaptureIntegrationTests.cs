// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.Fixture;

namespace Tests;

/// <summary>
/// An audited save captures its audit rows once when nothing could change the graph after the capture, and captures
/// again from the drain's last snapshot when a domain-event handler or a consumer processor could have.
/// </summary>
public sealed class AuditRecaptureIntegrationTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_detect_changes_once_before_the_base_save_when_audited_update_has_no_events(
        bool synchronous
    )
    {
        // given
        var detections = new DetectionCounter();
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync<AuditTestDbContext>(
            configure: null,
            configureServices: null,
            configureDbContext: options => detections.Attach(options),
            registration: AuditContextRegistration.Headless
        );
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var order = new Order { Id = Guid.NewGuid(), CustomerName = "before" };
        db.Orders.Add(order);
        await db.SaveChangesAsync(AbortToken);

        // when
        order.CustomerName = "after";
        detections.Count = 0;
        var written = synchronous ? db.SaveChanges() : await db.SaveChangesAsync(AbortToken);

        // then: the pipeline's own snapshot plus EF Core's pass inside the base save
        written.Should().Be(1);
        detections.Count.Should().Be(2);
        var rows = await db.Set<AuditLogEntry>().AsNoTracking().ToListAsync(AbortToken);
        rows.Should().ContainSingle(r => r.ChangeType == AuditChangeType.Updated);
    }

    [Fact]
    public async Task should_audit_entity_a_domain_event_handler_adds_during_the_save()
    {
        // given
        var (sp, conn) = await _CreateWithHandlersAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoteDbContext>();
        var note = new Note { Id = Guid.NewGuid(), Title = "draft" };
        note.Publish(addLine: true, retitle: null);
        db.Notes.Add(note);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        var rows = await db.Set<AuditLogEntry>().AsNoTracking().ToListAsync(AbortToken);
        rows.Select(r => r.EntityType).Should().BeEquivalentTo(typeof(Note).FullName, typeof(NoteLine).FullName);
    }

    [Fact]
    public async Task should_audit_value_a_domain_event_handler_writes_during_the_save()
    {
        // given
        var (sp, conn) = await _CreateWithHandlersAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoteDbContext>();
        var note = new Note { Id = Guid.NewGuid(), Title = "draft" };
        db.Notes.Add(note);
        await db.SaveChangesAsync(AbortToken);

        // when
        note.Body = "edited";
        note.Publish(addLine: false, retitle: "set by handler");
        await db.SaveChangesAsync(AbortToken);

        // then
        var updated = await db.Set<AuditLogEntry>()
            .AsNoTracking()
            .SingleAsync(r => r.ChangeType == AuditChangeType.Updated, AbortToken);
        updated.ChangedFields.Should().Contain(nameof(Note.Title));
        updated.NewValues![nameof(Note.Title)]!.ToString().Should().Be("set by handler");
    }

    [Fact]
    public async Task should_audit_value_a_consumer_processor_writes_on_the_clr_object()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync<AuditTestDbContext>(
            configure: null,
            configureServices: services =>
                services.AddHeadlessDbContextServices(options =>
                    options.AddSaveEntryProcessor<EmailNormalizingProcessor>(ServiceLifetime.Singleton)
                ),
            registration: AuditContextRegistration.Headless
        );
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var order = new Order { Id = Guid.NewGuid(), CustomerName = "before" };
        db.Orders.Add(order);
        await db.SaveChangesAsync(AbortToken);

        // when
        order.CustomerName = "after";
        await db.SaveChangesAsync(AbortToken);

        // then: the processor's plain property write is in the stored row and in its audit entry
        var updated = await db.Set<AuditLogEntry>()
            .AsNoTracking()
            .SingleAsync(r => r.ChangeType == AuditChangeType.Updated, AbortToken);
        updated.ChangedFields.Should().Contain(nameof(Order.Amount));
    }

    private static Task<(ServiceProvider Sp, Microsoft.Data.Sqlite.SqliteConnection Conn)> _CreateWithHandlersAsync()
    {
        return AuditIntegrationFixture.CreateAsync<NoteDbContext>(
            configure: null,
            configureServices: services =>
            {
                services.AddHeadlessDomainEventDispatcher();
                services.AddScoped<IDomainEventHandler<NotePublished>, NotePublishedHandler>();
            },
            registration: AuditContextRegistration.Headless
        );
    }

    private sealed class DetectionCounter
    {
        public int Count { get; set; }

        public void Attach(DbContextOptionsBuilder options)
        {
            options.LogTo(
                _ => Count++,
                (id, _) => id == CoreEventId.DetectChangesStarting,
                DbContextLoggerOptions.None
            );
        }
    }

    /// <summary>A consumer processor that writes a plain CLR property, which only change detection reveals.</summary>
    private sealed class EmailNormalizingProcessor : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is { State: EntityState.Modified, Entity: Order order })
            {
                order.Amount = 42;
            }
        }
    }

    public sealed record NotePublished(Guid NoteId, bool AddLine, string? Retitle);

    private sealed class NotePublishedHandler(NoteDbContext db) : IDomainEventHandler<NotePublished>
    {
        public ValueTask HandleAsync(EventContext<NotePublished> context, CancellationToken cancellationToken = default)
        {
            var payload = context.Payload;

            if (payload.AddLine)
            {
                db.Lines.Add(new NoteLine { Id = Guid.NewGuid(), NoteId = payload.NoteId });
            }

            if (payload.Retitle is not null)
            {
                db.Notes.Local.Single(n => n.Id == payload.NoteId).Title = payload.Retitle;
            }

            return ValueTask.CompletedTask;
        }
    }

    public sealed class Note : AggregateRoot<Guid>
    {
        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        public void Publish(bool addLine, string? retitle) => AddDomainEvent(new NotePublished(Id, addLine, retitle));
    }

    public sealed class NoteLine : Entity<Guid>
    {
        public Guid NoteId { get; set; }
    }

    public sealed class NoteDbContext(DbContextOptions options, IOptions<AuditLogStorageOptions> auditLogStorage)
        : HeadlessDbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();

        public DbSet<NoteLine> Lines => Set<NoteLine>();

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Note>().IsAudited();
            modelBuilder.Entity<NoteLine>().IsAudited();
            modelBuilder.ConfigureHeadlessAuditLog(
                auditLogStorage.Value,
                HeadlessStorageNaming.ForProvider(Database.ProviderName)
            );
            modelBuilder.Entity<AuditLogEntry>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).ValueGeneratedOnAdd();
            });
        }
    }
}
