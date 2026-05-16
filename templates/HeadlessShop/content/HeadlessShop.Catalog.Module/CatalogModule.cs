// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Processors;
using HeadlessShop.Catalog.Api;
using HeadlessShop.Catalog.Application;
using HeadlessShop.Catalog.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Catalog.Modules;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, string connectionString)
    {
        services.AddHeadlessDbContext<CatalogDbContext>(
            options => options.UseSqlite(connectionString),
            headless => headless.RemoveSaveEntryProcessor<HeadlessLocalEventSaveEntryProcessor>()
        );
        services.AddScoped<IValidator<CreateProductCommand>, CreateProductCommandValidator>();

        return services;
    }

    public static IEndpointRouteBuilder MapCatalogModule(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapCatalogEndpoints();
    }
}
