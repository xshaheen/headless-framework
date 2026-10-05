// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Respawn;
using Respawn.Graph;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// Configuration for <see cref="DatabaseReset"/>. Provides Respawner settings and
/// an optional connection provider for <see cref="HeadlessTestServer{TProgram}"/> integration.
/// </summary>
[PublicAPI]
public sealed class DatabaseResetOptions
{
    /// <summary>The database adapter. Defaults to <see cref="DbAdapter.Postgres"/>.</summary>
    public IDbAdapter DbAdapter { get; set; } = Respawn.DbAdapter.Postgres;

    /// <summary>
    /// Additional tables whose rows survive a reset, such as reference data the application seeds once at startup.
    /// The EF Core <c>__EFMigrationsHistory</c> table and the Headless <c>headless_schema_history</c> table are always
    /// preserved, in every schema: deleting either makes the next startup re-run work against a schema that already
    /// exists. A <see cref="Table"/> without a schema matches the name in every schema.
    /// </summary>
    public List<Table> TablesToPreserve { get; init; } = [];

    /// <summary>
    /// Whether <see cref="HeadlessTestServer{TProgram}.ResetDatabaseAsync"/> also preserves the host-state tables that
    /// framework features declare (<c>SchemaContribution.HostStateTables</c>): feature, permission, and setting
    /// definitions, which the host writes once at startup, and cluster membership rows, which the running host keeps
    /// heartbeating. Defaults to <see langword="true"/>. Set it to <see langword="false"/> only for a test that
    /// re-runs the startup work that writes them. Standalone <see cref="DatabaseReset"/> usage ignores it, because it
    /// has no service provider to read the declarations from.
    /// </summary>
    public bool PreserveHostStateTables { get; set; } = true;

    /// <summary>
    /// Factory for creating an <em>unopened</em> <see cref="DbConnection"/>.
    /// Required when using <see cref="HeadlessTestServer{TProgram}.ResetDatabaseAsync"/>;
    /// not needed for standalone <see cref="DatabaseReset"/> usage.
    /// </summary>
    public Func<IServiceProvider, DbConnection>? ConnectionProvider { get; set; }

    /// <summary>
    /// Optional predicate for provider-specific transient exceptions that are not a
    /// <see cref="DbException"/>, <see cref="IOException"/>, or <see cref="System.Net.Sockets.SocketException"/>.
    /// The built-in transient set is always applied before this predicate.
    /// </summary>
    public Func<Exception, bool>? AdditionalTransientExceptionFilter { get; set; }
}
