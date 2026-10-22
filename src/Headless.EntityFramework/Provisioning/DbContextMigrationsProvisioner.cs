// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>
/// Offers the EF Core migrations of <typeparamref name="TDbContext"/> as a resource provisioner of kind
/// <c>EntityFramework</c>: <c>resources provision</c> applies the pending migrations, and a host start fails while any is
/// pending, the way it fails on a missing schema step.
/// </summary>
/// <remarks>
/// The context is resolved from a scope of its own; when it is not registered, its <c>IDbContextFactory</c> is used.
/// Purge and delete are unsupported: the migrations do not say which tables hold the application's data, and deleting
/// the database would take every other owner's tables with it.
/// </remarks>
/// <typeparam name="TDbContext">The context whose migrations are applied.</typeparam>
internal sealed class DbContextMigrationsProvisioner<TDbContext>(IServiceProvider services) : IResourceProvisioner
    where TDbContext : DbContext
{
    public string Kind => "EntityFramework";

    public string Name => typeof(TDbContext).Name;

    // The context's tables are what the host's own services query first, so a pending migration stops the start.
    public ResourceStartupCheck StartupCheck => ResourceStartupCheck.FailStart;

    public bool CanPurge => false;

    public bool CanDelete => false;

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        await using var context = await _CreateAsync(cancellationToken).ConfigureAwait(false);
        await context.Context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        await using var context = await _CreateAsync(cancellationToken).ConfigureAwait(false);
        var pending = (
            await context.Context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)
        ).ToList();

        if (pending.Count > 0)
        {
            throw new ResourceProvisioningException(
                $"Migrations {string.Join(", ", pending)} of {typeof(TDbContext).Name} are not applied. Run "
                    + "'resources provision', or apply the migration script or bundle."
            );
        }
    }

    public Task PurgeAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("EF Core migrations do not say which tables to purge.");

    public Task DeleteAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Deleting the database would remove tables the migrations do not own.");

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var context = await _CreateAsync(cancellationToken).ConfigureAwait(false);
        var database = context.Context.Database;
        var applied = (await database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count();
        var pending = (await database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count();

        return
        [
            new("applied", applied.ToString(CultureInfo.InvariantCulture)),
            new("pending", pending.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    private async Task<ScopedContext> _CreateAsync(CancellationToken cancellationToken)
    {
        var scope = services.CreateAsyncScope();

        try
        {
            if (scope.ServiceProvider.GetService<TDbContext>() is { } registered)
            {
                return new(scope, registered, owned: false);
            }

            if (scope.ServiceProvider.GetService<IDbContextFactory<TDbContext>>() is { } factory)
            {
                return new(
                    scope,
                    await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false),
                    owned: true
                );
            }

            throw new InvalidOperationException(
                $"Unable to find {typeof(TDbContext).Name} or {typeof(IDbContextFactory<TDbContext>).Name} in the service provider."
            );
        }
        catch
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // A context the factory created is this provisioner's to dispose; a registered one belongs to the scope.
    private readonly struct ScopedContext(AsyncServiceScope scope, TDbContext context, bool owned) : IAsyncDisposable
    {
        public TDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            if (owned)
            {
                await Context.DisposeAsync().ConfigureAwait(false);
            }

            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
