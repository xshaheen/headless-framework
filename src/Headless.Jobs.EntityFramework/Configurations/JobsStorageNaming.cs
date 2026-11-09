// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Fencing;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs;

/// <summary>
/// The Jobs table names and the constraint and index names derived from them, in the casing of the database the
/// model targets: PascalCase on SQL Server (<c>TimeJobs</c>, <c>IX_TimeJobs_ExecutionTime</c>) and snake_case on
/// PostgreSQL (<c>time_jobs</c>, <c>ix_time_jobs_execution_time</c>).
/// </summary>
internal static class JobsStorageNaming
{
    internal const string TimeJobs = "TimeJobs";
    internal const string CronJobs = "CronJobs";
    internal const string CronJobOccurrences = "CronJobOccurrences";
    internal const string TimeJobIdempotencyReservations = "TimeJobIdempotencyReservations";

    /// <summary>The naming style of the database <paramref name="context"/> targets.</summary>
    internal static StorageNamingStyle StyleOf(DbContext context) =>
        HeadlessStorageNaming.ForProvider(context.Database.ProviderName);

    /// <summary>The default name of a Jobs table in <paramref name="style"/>.</summary>
    internal static string Table(StorageNamingStyle style, string pascalTable) =>
        HeadlessStorageNaming.Apply(style, pascalTable);

    /// <summary>
    /// A constraint or index name: <c>{PREFIX}_{table}_{Part}...</c> or <c>{prefix}_{table}_{part}...</c>. The table
    /// name is used as given, because it is already resolved for the database and may be a consumer's own name.
    /// </summary>
    internal static string Name(
        StorageNamingStyle style,
        string pascalPrefix,
        string tableName,
        params ReadOnlySpan<string> pascalParts
    )
    {
        var builder = new StringBuilder(HeadlessStorageNaming.Apply(style, pascalPrefix)).Append('_').Append(tableName);

        foreach (var part in pascalParts)
        {
            builder.Append('_').Append(HeadlessStorageNaming.Apply(style, part));
        }

        return builder.ToString();
    }

    /// <summary>A column name converted to <paramref name="style"/> and delimited with ANSI double quotes.</summary>
    /// <remarks>
    /// Used only in filter SQL built before the model is finalized. SQL Server accepts the double quotes because EF
    /// Core connections run with <c>QUOTED_IDENTIFIER ON</c>.
    /// </remarks>
    internal static string QuotedColumn(StorageNamingStyle style, string pascalColumn) =>
        "\"" + HeadlessStorageNaming.Apply(style, pascalColumn) + "\"";

    /// <summary>
    /// The lease columns of a Jobs table in <paramref name="style"/>: <c>lease_generation</c> and its siblings on
    /// PostgreSQL, <c>LeaseGeneration</c> and its siblings elsewhere.
    /// </summary>
    /// <remarks>
    /// <see cref="ApplyJobsColumnNaming"/> renames scalar properties only, so the lease, a complex property, takes its
    /// names here.
    /// </remarks>
    internal static FencedRowColumns LeaseColumns(StorageNamingStyle style) =>
        new()
        {
            Generation = HeadlessStorageNaming.Apply(style, "LeaseGeneration"),
            State = HeadlessStorageNaming.Apply(style, "LeaseState"),
            ExpiresAt = HeadlessStorageNaming.Apply(style, "LeaseExpiresAt"),
            Holder = HeadlessStorageNaming.Apply(style, "LeaseHolder"),
            TakeoverCount = HeadlessStorageNaming.Apply(style, "LeaseTakeoverCount"),
        };

    extension(EntityTypeBuilder builder)
    {
        /// <summary>
        /// Names every mapped column after its property in <paramref name="style"/>, so a PostgreSQL database gets
        /// unquoted-friendly snake_case columns and SQL Server keeps the PascalCase property names.
        /// </summary>
        /// <remarks>
        /// Call last in an entity configuration: it walks the properties already on the entity, including a
        /// consumer's derived-entity properties, while a consumer mapping applied afterwards can still rename one.
        /// </remarks>
        internal void ApplyJobsColumnNaming(StorageNamingStyle style)
        {
            foreach (var property in builder.Metadata.GetProperties().ToList())
            {
                builder.Property(property.Name).HasColumnName(HeadlessStorageNaming.Apply(style, property.Name));
            }
        }
    }
}
