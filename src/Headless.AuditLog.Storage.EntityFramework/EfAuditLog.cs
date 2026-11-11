// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog.Internal;
using Headless.Checks;
using Headless.Context;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

internal sealed class EfAuditLog<TContext>(
    TContext context,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider,
    IOptions<AuditLogOptions> options,
    IEnumerable<IAuditLogEntryEnricher> enrichers,
    IAuditClientInfoProvider? clientInfo = null
) : IAuditLog<TContext>
    where TContext : DbContext
{
    /// <inheritdoc />
    public Task LogAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!options.Value.IsEnabled)
        {
            return Task.CompletedTask;
        }

        var entry = context
            .Set<AuditLogEntry>()
            .Add(
                ExplicitAuditLogEntry.Create(
                    request,
                    currentUser,
                    currentTenant,
                    correlationIdProvider,
                    timeProvider,
                    clientInfo
                )
            );

        try
        {
            enrichers.Enrich(entry);
        }
        catch
        {
            // The caller may catch this and still save: the row must not go out with its columns half filled.
            entry.State = EntityState.Detached;

            throw;
        }

        return Task.CompletedTask;
    }
}
