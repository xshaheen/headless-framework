// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>
/// Enforces a tenant-routed context's pin at the database boundary. Every connection open checks the ambient tenant
/// and that the connection reaches the pinned database; every command checks the ambient tenant again, because a
/// command on an already-open connection, raw SQL, or a query over an entity that is not tenant-owned never reads
/// the context's tenant id and would otherwise run against the pinned store under another tenant.
/// </summary>
internal sealed class HeadlessTenantPlacementInterceptor : DbConnectionInterceptor, IDbCommandInterceptor
{
    public static HeadlessTenantPlacementInterceptor Instance { get; } = new();

    private HeadlessTenantPlacementInterceptor() { }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result
    )
    {
        _VerifyConnection(eventData.Context, connection);

        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        _VerifyConnection(eventData.Context, connection);

        return ValueTask.FromResult(result);
    }

    // EF creates a DbCommand for every execution (queries, SaveChanges batches, raw SQL, migrations), so this one
    // synchronous hook covers the sync and async execution paths alike.
    public InterceptionResult<DbCommand> CommandCreating(
        CommandCorrelatedEventData eventData,
        InterceptionResult<DbCommand> result
    )
    {
        _Placement(eventData.Context)?.GetTenantId(_AmbientTenantId(eventData.Context));

        return result;
    }

    private static void _VerifyConnection(DbContext? context, DbConnection connection)
    {
        if (_Placement(context) is not { } placement)
        {
            return;
        }

        placement.GetTenantId(_AmbientTenantId(context));

        // Only the context's own connection carries its data. Providers open separate administrative connections
        // to a maintenance database (for example to run CREATE DATABASE for EnsureCreated or Migrate), and those
        // must not be held to the tenant database.
        if (ReferenceEquals(connection, context!.Database.GetDbConnection()))
        {
            placement.VerifyConnection(connection);
        }
    }

    private static HeadlessRoutedPlacement? _Placement(DbContext? context) =>
        context is HeadlessDbContext headless ? headless.RoutedPlacement : null;

    private static string? _AmbientTenantId(DbContext? context) =>
        context is HeadlessDbContext headless ? headless.AmbientTenantId : null;
}
