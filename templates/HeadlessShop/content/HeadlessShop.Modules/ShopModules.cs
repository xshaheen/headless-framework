// Copyright (c) Mahmoud Shaheen. All rights reserved.

using HeadlessShop.Catalog.Modules;
using HeadlessShop.Ordering.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Modules;

public static class ShopModules
{
    public static IServiceCollection AddShopModules(this IServiceCollection services, string connectionString)
    {
        services.AddCatalogModule(connectionString);
        services.AddOrderingModule(connectionString);

        return services;
    }

    public static IEndpointRouteBuilder MapShopModules(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCatalogModule();
        endpoints.MapOrderingModule();

        return endpoints;
    }
}
