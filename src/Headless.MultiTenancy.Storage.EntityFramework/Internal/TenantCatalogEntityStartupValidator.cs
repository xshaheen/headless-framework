// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy.Internal;

// Resolves the factory inside ValidateAsync rather than injecting it: the startup runner constructs every validator
// up front, so a missing factory in the constructor would fail the runner itself with an opaque activation error
// before the required-service and singleton-lifetime checks could report it.
internal sealed class TenantCatalogEntityStartupValidator<TContext>(IServiceProvider services)
    : IHeadlessStartupValidator
    where TContext : DbContext
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>();

        if (dbFactory is null)
        {
            return;
        }

        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (context.Model.FindAnnotation(TenantCatalogStorageModelAnnotations.IsConfigured)?.Value is not true)
        {
            throw new InvalidOperationException(
                $"Headless.MultiTenancy: the registered DbContext `{context.GetType().FullName}` has not fully configured `{nameof(TenantRecord)}`. "
                    + "Call `modelBuilder.AddHeadlessTenancyCatalog(this)` in your `OnModelCreating`."
            );
        }
    }
}
