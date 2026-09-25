// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Validation;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog.Internal;

internal sealed class AuditLogEntityStartupValidator<TContext>(IDbContextFactory<TContext> dbFactory)
    : IHeadlessStartupValidator
    where TContext : DbContext
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
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
}
