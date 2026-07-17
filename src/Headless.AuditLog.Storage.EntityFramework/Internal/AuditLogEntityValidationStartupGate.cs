// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Headless.AuditLog.Internal;

internal sealed class AuditLogEntityValidationStartupGate<TContext>(IDbContextFactory<TContext> dbFactory)
    : IHostedLifecycleService
    where TContext : DbContext
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (context.Model.FindAnnotation(AuditLogStorageModelAnnotations.IsConfigured)?.Value is not true)
        {
            throw new InvalidOperationException(
                $"Headless.AuditLog: the registered DbContext `{context.GetType().FullName}` has not fully configured `{nameof(AuditLogEntry)}`. "
                    + "Call `modelBuilder.AddHeadlessAuditLog(auditLogStorageOptions)` in your `OnModelCreating`."
            );
        }
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
