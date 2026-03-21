// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.EntityFramework;

/// <summary>
/// Shared audit save pipeline logic used by both <c>HeadlessDbContext</c> and
/// <c>HeadlessIdentityDbContext</c>. Ensures a single implementation for
/// capture → resolve → persist.
/// </summary>
internal static class AuditSavePipelineHelper
{
    /// <summary>
    /// Captures audit entries from the change tracker before SaveChanges.
    /// Returns <c>null</c> when audit capture is not registered or fails.
    /// </summary>
    public static IReadOnlyList<AuditLogEntryData>? CaptureAuditEntries(DbContext context, ILogger? logger)
    {
        var auditCapture = _GetServiceOrNull<IAuditChangeCapture>(context);

        if (auditCapture is null)
            return null;

        var currentUser = _GetServiceOrNull<ICurrentUser>(context);
        var currentTenant = _GetServiceOrNull<ICurrentTenant>(context);
        var correlationIdProvider = _GetServiceOrNull<ICorrelationIdProvider>(context);
        var clock = _GetServiceOrNull<IClock>(context);
        var timestamp = clock?.UtcNow ?? DateTimeOffset.UtcNow;

        try
        {
            return auditCapture.CaptureChanges(
                context.ChangeTracker.Entries().Select(static e => (object)e),
                currentUser?.UserId?.ToString(),
                currentUser?.AccountId?.ToString(),
                currentTenant?.Id,
                correlationIdProvider?.CorrelationId,
                timestamp
            );
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Audit change capture failed. Continuing with entity save.");
            return null;
        }
    }

    /// <summary>
    /// Detaches stale audit entries from a prior failed attempt before an execution strategy retry.
    /// </summary>
    public static void PrepareForRetry(DbContext context)
    {
        var store = _GetServiceOrNull<IAuditLogStore>(context);
        store?.PrepareForRetry(context);
    }

    /// <summary>
    /// Resolves deferred entity IDs on captured audit entries after entity SaveChanges
    /// has assigned store-generated keys.
    /// </summary>
    public static void ResolveEntityIds(DbContext context, IReadOnlyList<AuditLogEntryData> entries)
    {
        if (_GetServiceOrNull<IAuditChangeCapture>(context) is IAuditEntityIdResolver resolver)
            resolver.ResolveEntityIds(entries);
    }

    /// <summary>
    /// Adds audit entries to the saving context for synchronous persist.
    /// </summary>
    public static void SaveAuditEntries(DbContext context, IReadOnlyList<AuditLogEntryData> entries)
    {
        var store = _GetServiceOrNull<IAuditLogStore>(context);
        store?.Save(entries, context);
    }

    /// <summary>
    /// Adds audit entries to the saving context for asynchronous persist.
    /// </summary>
    public static async Task SaveAuditEntriesAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData> entries,
        CancellationToken cancellationToken
    )
    {
        var store = _GetServiceOrNull<IAuditLogStore>(context);

        if (store is not null)
            await store.SaveAsync(entries, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a service from the DbContext's service provider, returning <c>null</c> when not
    /// registered. Mirrors EF Core's internal resolution strategy (internal provider first, then
    /// application provider via <c>CoreOptionsExtension.ApplicationServiceProvider</c>) but uses
    /// the non-throwing <see cref="IServiceProvider.GetService"/> instead of
    /// <c>GetRequiredService</c>.
    /// </summary>
    private static T? _GetServiceOrNull<T>(DbContext context)
        where T : class
    {
        var internalProvider = ((IInfrastructure<IServiceProvider>)context).Instance;

        if (internalProvider.GetService(typeof(T)) is T service)
            return service;

        // Fall back to the application service provider (same as EF Core's internal
        // InfrastructureExtensions.GetService does before throwing).
        var appProvider = internalProvider.GetService(typeof(IDbContextOptions)) is IDbContextOptions options
            ? options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            : null;

        return (T?)appProvider?.GetService(typeof(T));
    }
}
