// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Headless.MultiTenancy.Internal;

internal sealed class TenantCatalogEntityValidationStartupGate<TContext>(IDbContextFactory<TContext> dbFactory)
    : IHostedLifecycleService
    where TContext : DbContext
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (context.Model.FindAnnotation(TenantCatalogStorageModelAnnotations.IsConfigured)?.Value is not true)
        {
            throw new InvalidOperationException(
                $"Headless.MultiTenancy: the registered DbContext `{context.GetType().FullName}` has not fully configured `{nameof(TenantRecord)}`. "
                    + "Call `modelBuilder.AddHeadlessTenancyCatalog(this)` in your `OnModelCreating`."
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
