// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application entity, DbContext, and variables the audit log guide's examples assume.

global using static AuditLogAmbient;
using Headless.Abstractions;
using Headless.AuditLog;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public sealed class Patient
{
    public Guid Id { get; set; }

    public string NationalId { get; set; } = "";

    public string CreditCardToken { get; set; } = "";

    public DateTimeOffset LastComputedAt { get; set; }
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class AuditLogAmbient
{
    public static IAuditLog<AppDbContext> auditLog => null!;

    public static IReadAuditLog<AppDbContext> readAuditLog => null!;

    public static ICurrentUser currentUser => null!;

    public static HeadlessAuditLogSetupBuilder setup => null!;

    public static ModelBuilder modelBuilder => null!;

    public static string connectionString => "";

    public static string tenantId => "";

    public static Guid id => default;

    public static Guid patientId => default;

    public static void Render(IReadOnlyList<AuditLogEntryData> entries) { }
}
