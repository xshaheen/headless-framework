using HeadlessShop.Catalog.Api;
using HeadlessShop.Catalog.Modules;
using HeadlessShop.Ordering.Api;
using HeadlessShop.Ordering.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Modules;

public static class SetupShopModules
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddShopModules(string connectionString)
        {
            services.AddCatalogModule(connectionString);
            services.AddOrderingModule(connectionString);

            return services;
        }
    }
}

public static class ShopEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapShopModules()
        {
            endpoints.MapCatalogEndpoints();
            endpoints.MapOrderingEndpoints();

            return endpoints;
        }
    }
}
