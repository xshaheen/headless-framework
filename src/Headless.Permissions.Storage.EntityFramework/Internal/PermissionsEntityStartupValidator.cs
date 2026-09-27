// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Validation;
using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Permissions.Internal;

internal sealed class PermissionsEntityStartupValidator<TContext>(IDbContextFactory<TContext> dbFactory)
    : IHeadlessStartupValidator
    where TContext : DbContext
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        _EnsureEntity(context, typeof(PermissionGrantRecord), nameof(PermissionGrantRecord));
        _EnsureEntity(context, typeof(PermissionDefinitionRecord), nameof(PermissionDefinitionRecord));
        _EnsureEntity(context, typeof(PermissionGroupDefinitionRecord), nameof(PermissionGroupDefinitionRecord));
    }

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
