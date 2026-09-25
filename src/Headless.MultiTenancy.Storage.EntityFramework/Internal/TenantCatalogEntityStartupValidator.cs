// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Validation;
using Microsoft.EntityFrameworkCore;

namespace Headless.MultiTenancy.Internal;

internal sealed class TenantCatalogEntityStartupValidator<TContext>(IDbContextFactory<TContext> dbFactory)
    : IStartupValidator
    where TContext : DbContext
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
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
}
