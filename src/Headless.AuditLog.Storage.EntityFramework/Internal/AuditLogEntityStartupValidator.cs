// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.Internal;

// Resolves the factory inside ValidateAsync rather than injecting it: the startup runner constructs every validator
// up front, so a missing factory in the constructor would fail the runner itself with an opaque activation error
// before the required-service and singleton-lifetime checks could report it.
internal sealed class AuditLogEntityStartupValidator<TContext>(IServiceProvider services) : IHeadlessStartupValidator
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

        if (context.Model.FindAnnotation(AuditLogStorageModelAnnotations.IsConfigured)?.Value is not true)
        {
            throw new InvalidOperationException(
                $"Headless.AuditLog: the registered DbContext `{context.GetType().FullName}` has not fully configured `{nameof(AuditLogEntry)}`. "
                    + "Call `modelBuilder.ConfigureHeadlessAuditLog(this)` in your `OnModelCreating`."
            );
        }

        // No I/O: the provider name comes from the context's options. A registration made for another database
        // would purge without passing that database's append-only trigger, and would offer no partition service.
        var registered = scope.ServiceProvider.GetService<EfAuditLogDatabase>()?.Registered;
        var actual = EfAuditLogDatabase.Provider(context);

        if (registered != actual)
        {
            throw new InvalidOperationException(
                $"Headless.AuditLog: the registered DbContext `{context.GetType().FullName}` runs on `{context.Database.ProviderName}`, "
                    + $"but the audit log storage was registered with {EfAuditLogDatabase.RegistrationOf(registered)}. "
                    + $"Register it with {EfAuditLogDatabase.RegistrationOf(actual)} instead."
            );
        }

        // Only PostgreSQL has the owner's purge function.
        var storageOptions = scope.ServiceProvider.GetService<IOptions<AuditLogStorageOptions>>()?.Value;

        if (storageOptions?.PurgeAuthority is AuditLogPurgeAuthority.Owner && actual is not StorageProvider.PostgreSql)
        {
            throw new InvalidOperationException(
                $"Headless.AuditLog: {nameof(AuditLogStorageOptions.PurgeAuthority)} {nameof(AuditLogPurgeAuthority.Owner)} is supported on PostgreSQL only, and `{context.GetType().FullName}` runs on `{context.Database.ProviderName}`."
            );
        }
    }
}
