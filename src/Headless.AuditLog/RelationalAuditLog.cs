// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Headless.MultiTenancy;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

/// <summary>
/// The relational <see cref="IAuditLog{TContext}"/>: writes each entry in the transaction of the scope's
/// <typeparamref name="TContext"/>, so it commits or rolls back with the caller's changes. Without one, it writes on a
/// separate connection or throws, as <see cref="AuditLogOptions.MissingTransactionStrategy"/> says.
/// </summary>
internal sealed class RelationalAuditLog<TContext>(
    IServiceProvider services,
    RelationalAuditLogEnlistment enlistment,
    RelationalAuditLogWriter writer,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider,
    IOptions<AuditLogOptions> options
) : IAuditLog<TContext>
{
    public Task LogAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(request);

        if (!options.Value.IsEnabled)
        {
            return Task.CompletedTask;
        }

        // The scope's context is the one the caller saves through, the same instance the EF storage adds its entry to.
        var (connection, transaction) = enlistment.Resolve(services.GetService(typeof(TContext)), typeof(TContext));
        var entry = RelationalExplicitAuditLogEntry.Create(
            request,
            currentUser,
            currentTenant,
            correlationIdProvider,
            timeProvider
        );

        return writer.WriteAsync([entry], connection, transaction, cancellationToken);
    }
}

/// <summary>
/// The relational <see cref="IAuditLogWriter{TContext}"/>: commits each entry on its own connection, whatever
/// transaction the caller holds.
/// </summary>
internal sealed class RelationalStandaloneAuditLog<TContext>(
    RelationalAuditLogWriter writer,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider,
    IOptions<AuditLogOptions> options
) : IAuditLogWriter<TContext>
{
    public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(request);

        if (!options.Value.IsEnabled)
        {
            return Task.CompletedTask;
        }

        var entry = RelationalExplicitAuditLogEntry.Create(
            request,
            currentUser,
            currentTenant,
            correlationIdProvider,
            timeProvider
        );

        return writer.WriteAsync([entry], cancellationToken: cancellationToken);
    }
}

/// <summary>Builds the stored entry for an explicit event, stamped with the ambient user, tenant, and correlation id.</summary>
internal static class RelationalExplicitAuditLogEntry
{
    public static AuditLogEntryData Create(
        AuditLogWriteRequest request,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        ICorrelationIdProvider correlationIdProvider,
        TimeProvider timeProvider
    )
    {
        return new AuditLogEntryData
        {
            CreatedAt = timeProvider.GetUtcNow(),
            UserId = AuditLogFieldLimits.Truncate(currentUser.UserId?.ToString(), AuditLogFieldLimits.UserId),
            AccountId = AuditLogFieldLimits.Truncate(currentUser.AccountId?.ToString(), AuditLogFieldLimits.AccountId),
            TenantId = AuditLogFieldLimits.Truncate(currentTenant.Id, AuditLogFieldLimits.TenantId),
            CorrelationId = AuditLogFieldLimits.Truncate(
                correlationIdProvider.CorrelationId,
                AuditLogFieldLimits.CorrelationId
            ),
            Action = AuditLogFieldLimits.Truncate(request.Action, AuditLogFieldLimits.Action),
            EntityType = AuditLogFieldLimits.Truncate(request.EntityType, AuditLogFieldLimits.EntityType),
            EntityId = AuditLogFieldLimits.Truncate(request.EntityId, AuditLogFieldLimits.EntityId),
            NewValues = request.Data,
            Success = request.Success,
            ErrorCode = AuditLogFieldLimits.Truncate(request.ErrorCode, AuditLogFieldLimits.ErrorCode),
        };
    }
}
