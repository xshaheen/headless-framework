// Copyright (c) Mahmoud Shaheen. All rights reserved.

using HeadlessShop.Catalog.Infrastructure;
using HeadlessShop.Ordering.Infrastructure;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Modules;

public static class ShopDatabaseInitializer
{
    public static async Task InitializeShopDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        await catalog.Database.EnsureCreatedAsync(cancellationToken);

        if (!await _HasTableAsync(ordering, "Orders", cancellationToken))
        {
            var orderingCreator = ordering.Database.GetService<IRelationalDatabaseCreator>();
            await orderingCreator.CreateTablesAsync(cancellationToken);
        }
    }

    private static async Task<bool> _HasTableAsync(DbContext context, string tableName, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName";

            var tableNameParameter = command.CreateParameter();
            tableNameParameter.ParameterName = "$tableName";
            tableNameParameter.Value = tableName;
            command.Parameters.Add(tableNameParameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);

            return Convert.ToInt64(result, CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
