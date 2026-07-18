// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// Minimal DbContext exposing the <see cref="AuditLogEntry"/> model for store/log unit tests.
/// SQLite cannot autoincrement a member of the shipped composite (CreatedAt, Id) primary key,
/// so the key is overridden to a single-column PK on Id — the documented SQLite consumer override.
/// </summary>
public sealed class AuditStoreDbContext(DbContextOptions<AuditStoreDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddHeadlessAuditLog(new AuditLogStorageOptions());
        modelBuilder.Entity<AuditLogEntry>().HasKey(e => e.Id);
    }

    // Each test gets its own in-memory SQLite connection so databases are isolated
    public static (AuditStoreDbContext db, SqliteConnection conn) Create()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        var builder = new DbContextOptionsBuilder<AuditStoreDbContext>().UseSqlite(conn);

        var db = new AuditStoreDbContext(builder.Options);
#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
        db.Database.EnsureCreated();
#pragma warning restore MA0045
        return (db, conn);
    }
}
