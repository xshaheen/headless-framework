// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sql;

namespace Tests;

/// <summary>
/// What a provider leaf supplies to run <see cref="FencedRowsConformanceTests{TFixture}" />: the engine's dialect and a
/// database holding a consumer table named <see cref="WorkItemsTable" /> in <see cref="Schema" />, with a generation
/// sequence named <see cref="GenerationSequence" />.
/// </summary>
/// <remarks>
/// The table has a Guid primary key <c>id</c>, a text <c>status</c>, a nullable Guid <c>parent_id</c>, and the five
/// lease columns under their default names: <c>lease_generation</c> (bigint, default 0), <c>lease_state</c> (smallint,
/// default 0), <c>lease_expires_at</c> (nullable instant), <c>lease_holder</c> (nullable text of up to 200
/// characters), and <c>lease_takeover_count</c> (int, default 0). The suite inserts and reads its own rows through
/// the dialect, so the fixture owns only the DDL.
/// </remarks>
public interface IFencedRowsFixture
{
    /// <summary>The consumer table's unquoted name.</summary>
    const string WorkItemsTable = "work_items";

    /// <summary>The unquoted name of the generation sequence, in <see cref="Schema" />.</summary>
    const string GenerationSequence = "work_item_generations";

    /// <summary>The engine's dialect.</summary>
    ISqlDialect Dialect { get; }

    /// <summary>The unquoted schema holding the table and the sequence.</summary>
    string Schema { get; }

    /// <summary>
    /// Whether a claim skips rows another transaction holds locked. SQLite has no row locks: a claim takes the database
    /// write lock, so a second claimer waits for the first to commit instead.
    /// </summary>
    bool ClaimSkipsLockedRows => true;

    /// <summary>
    /// Returns the unquoted name by which <paramref name="name" />, in <see cref="Schema" />, resolves without a schema:
    /// the name itself where <see cref="Schema" /> is the connection's default schema, and the dialect's flattened name
    /// on SQLite, which has no schemas.
    /// </summary>
    string UnqualifiedName(string name) => name;

    /// <summary>Creates an unopened connection to the database that holds the table.</summary>
    DbConnection CreateConnection();
}
