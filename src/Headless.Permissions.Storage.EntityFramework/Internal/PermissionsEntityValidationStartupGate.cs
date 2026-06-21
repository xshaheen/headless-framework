// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Headless.Permissions.Internal;

internal sealed class PermissionsEntityValidationStartupGate<TContext>(IDbContextFactory<TContext> dbFactory)
    : IHostedLifecycleService
    where TContext : DbContext
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        _EnsureEntity(context, typeof(PermissionGrantRecord), nameof(PermissionGrantRecord));
        _EnsureEntity(context, typeof(PermissionDefinitionRecord), nameof(PermissionDefinitionRecord));
        _EnsureEntity(context, typeof(PermissionGroupDefinitionRecord), nameof(PermissionGroupDefinitionRecord));
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void _EnsureEntity(DbContext context, Type entityType, string entityName)
    {
        if (context.Model.FindEntityType(entityType) is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Headless.Permissions: the registered DbContext `{context.GetType().FullName}` does not contain `{entityName}`. "
                + "Call `modelBuilder.AddHeadlessPermissions(permissionsStorageOptions)` in your `OnModelCreating`."
        );
    }
}
