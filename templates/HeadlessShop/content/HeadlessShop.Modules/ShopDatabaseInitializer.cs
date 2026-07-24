using HeadlessShop.Catalog.Infrastructure;
using HeadlessShop.Ordering.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Modules;

public static class ShopDatabaseInitializer
{
    extension(IServiceProvider services)
    {
        public async Task InitializeShopDatabaseAsync(CancellationToken cancellationToken = default)
        {
            await using var scope = services.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

            await catalog.Database.MigrateAsync(cancellationToken);
            await ordering.Database.MigrateAsync(cancellationToken);
        }
    }
}
