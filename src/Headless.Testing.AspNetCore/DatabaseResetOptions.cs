// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Respawn;
using Respawn.Graph;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// Configuration for <see cref="DatabaseReset"/>. Provides Respawner settings and
/// an optional connection provider for <see cref="HeadlessTestServer{TProgram}"/> integration.
/// </summary>
public sealed class DatabaseResetOptions
{
    /// <summary>The database adapter. Defaults to <see cref="DbAdapter.Postgres"/>.</summary>
    public IDbAdapter DbAdapter { get; set; } = Respawn.DbAdapter.Postgres;

    /// <summary>
    /// Additional tables to exclude from reset. <c>__EFMigrationsHistory</c> is always excluded
    /// automatically — no need to add it here.
    /// </summary>
    public List<Table> TablesToIgnore { get; set; } = [];

    /// <summary>
    /// Factory for creating an <em>unopened</em> <see cref="DbConnection"/>.
    /// Required when using <see cref="HeadlessTestServer{TProgram}.ResetDatabaseAsync"/>;
    /// not needed for standalone <see cref="DatabaseReset"/> usage.
    /// </summary>
    public Func<IServiceProvider, DbConnection>? ConnectionProvider { get; set; }
}
