// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MigrateDbContextExtensions
{
    public static async Task MigrateDbContextAsync<TContext>(
        this IServiceProvider services,
        CancellationToken token = default
    )
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.Database.MigrateAsync(token);
    }
}
