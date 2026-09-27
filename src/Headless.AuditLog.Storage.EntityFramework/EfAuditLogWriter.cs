// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog.Internal;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

// A context of its own keeps the write out of the caller's change tracker: saving the scoped context would also
// flush whatever entity changes the caller had pending.
internal sealed class EfAuditLogWriter<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider,
    IOptions<AuditLogOptions> options
) : IAuditLogWriter<TContext>
    where TContext : DbContext
{
    /// <inheritdoc />
    public async Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(request);

        if (!options.Value.IsEnabled)
        {
            return;
        }

        var entry = ExplicitAuditLogEntry.Create(
            request,
            currentUser,
            currentTenant,
            correlationIdProvider,
            timeProvider
        );

        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Set<AuditLogEntry>().Add(entry);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
