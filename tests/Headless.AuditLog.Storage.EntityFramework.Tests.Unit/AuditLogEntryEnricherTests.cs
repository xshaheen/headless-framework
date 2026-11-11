// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Context;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class AuditLogEntryEnricherTests : TestBase
{
    private static readonly DateTimeOffset _Timestamp = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_fill_an_application_column_on_every_change_capture_row()
    {
        // given
        var (db, conn) = ExtendedAuditDbContext.Create();
        await using var connection = conn;
        await using var context = db;
        var store = new EfAuditLogStore([new CategoryEnricher()]);

        // when
        store.Save(
            [
                new AuditLogEntryData { Action = "order.created", CreatedAt = _Timestamp },
                new AuditLogEntryData { Action = "user.created", CreatedAt = _Timestamp },
            ],
            context
        );
        await context.SaveChangesAsync(AbortToken);

        // then
        (await _CategoriesAsync(context))
            .Should()
            .Equal("order", "user");
    }

    [Fact]
    public async Task should_fill_an_application_column_on_an_explicit_event()
    {
        // given
        var (db, conn) = ExtendedAuditDbContext.Create();
        await using var connection = conn;
        await using var context = db;
        var auditLog = _CreateAuditLog(context, new CategoryEnricher());

        // when
        await auditLog.LogAsync(new AuditLogWriteRequest { Action = "pii.revealed" }, AbortToken);
        await context.SaveChangesAsync(AbortToken);

        // then
        (await _CategoriesAsync(context))
            .Should()
            .Equal("pii");
    }

    [Fact]
    public async Task should_run_enrichers_in_registration_order()
    {
        // given - the second enricher reads what the first one set
        var (db, conn) = ExtendedAuditDbContext.Create();
        await using var connection = conn;
        await using var context = db;
        var auditLog = _CreateAuditLog(context, new CategoryEnricher(), new UpperCaseCategoryEnricher());

        // when
        await auditLog.LogAsync(new AuditLogWriteRequest { Action = "pii.revealed" }, AbortToken);
        await context.SaveChangesAsync(AbortToken);

        // then
        (await _CategoriesAsync(context))
            .Should()
            .Equal("PII");
    }

    [Fact]
    public async Task should_leave_no_row_behind_when_an_enricher_fails()
    {
        // given
        var (db, conn) = ExtendedAuditDbContext.Create();
        await using var connection = conn;
        await using var context = db;
        var store = new EfAuditLogStore([new FailingEnricher()]);
        var auditLog = _CreateAuditLog(context, new FailingEnricher());

        // when
        var capture = () =>
            store.Save([new AuditLogEntryData { Action = "order.created", CreatedAt = _Timestamp }], context);
        var explicitEvent = () => auditLog.LogAsync(new AuditLogWriteRequest { Action = "pii.revealed" }, AbortToken);

        // then - a caller that catches the failure and still saves writes no half-filled row
        capture.Should().Throw<InvalidOperationException>().WithMessage("no category");
        await explicitEvent.Should().ThrowAsync<InvalidOperationException>().WithMessage("no category");
        context.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();
    }

    private static EfAuditLog<ExtendedAuditDbContext> _CreateAuditLog(
        ExtendedAuditDbContext context,
        params IAuditLogEntryEnricher[] enrichers
    )
    {
        return new EfAuditLog<ExtendedAuditDbContext>(
            context,
            Substitute.For<ICurrentUser>(),
            Substitute.For<ICurrentTenant>(),
            Substitute.For<ICorrelationIdProvider>(),
            new FakeTimeProvider(_Timestamp),
            Options.Create(new AuditLogOptions()),
            enrichers
        );
    }

    private Task<List<string>> _CategoriesAsync(ExtendedAuditDbContext context)
    {
        return context
            .Set<AuditLogEntry>()
            .AsNoTracking()
            .OrderBy(entry => entry.Id)
            .Select(entry => EF.Property<string>(entry, ExtendedAuditDbContext.Category))
            .ToListAsync(AbortToken);
    }

    /// <summary>The audit model with one column the application added.</summary>
    private sealed class ExtendedAuditDbContext(DbContextOptions<ExtendedAuditDbContext> options) : DbContext(options)
    {
        public const string Category = "Category";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessAuditLog(new AuditLogStorageOptions(), StorageNamingStyle.PascalCase);
            // SQLite cannot autoincrement a member of the composite key, so the key is the documented SQLite override.
            modelBuilder.Entity<AuditLogEntry>().HasKey(entry => entry.Id);
            modelBuilder.Entity<AuditLogEntry>().Property<string>(Category).HasMaxLength(32).IsRequired();
        }

        public static (ExtendedAuditDbContext Db, SqliteConnection Connection) Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var db = new ExtendedAuditDbContext(
                new DbContextOptionsBuilder<ExtendedAuditDbContext>().UseSqlite(connection).Options
            );
            db.Database.EnsureCreated();

            return (db, connection);
        }
    }

    /// <summary>Files a row under the first segment of its action.</summary>
    private sealed class CategoryEnricher : IAuditLogEntryEnricher
    {
        public void Enrich(EntityEntry<AuditLogEntry> entry)
        {
            entry.Property<string>(ExtendedAuditDbContext.Category).CurrentValue = entry.Entity.Action.Split('.')[0];
        }
    }

    private sealed class UpperCaseCategoryEnricher : IAuditLogEntryEnricher
    {
        public void Enrich(EntityEntry<AuditLogEntry> entry)
        {
            var category = entry.Property<string>(ExtendedAuditDbContext.Category);
            category.CurrentValue = category.CurrentValue.ToUpperInvariant();
        }
    }

    private sealed class FailingEnricher : IAuditLogEntryEnricher
    {
        public void Enrich(EntityEntry<AuditLogEntry> entry)
        {
            throw new InvalidOperationException("no category");
        }
    }
}
