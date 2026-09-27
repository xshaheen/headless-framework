// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog.Internal;
using Headless.Checks;
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
    IOptions<AuditLogOptions> options
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

        context
            .Set<AuditLogEntry>()
            .Add(
                ExplicitAuditLogEntry.Create(request, currentUser, currentTenant, correlationIdProvider, timeProvider)
            );

        return Task.CompletedTask;
    }
}
