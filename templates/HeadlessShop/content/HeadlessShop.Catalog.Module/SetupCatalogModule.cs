using FluentValidation;
using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Processors;
using HeadlessShop.Catalog.Application;
using HeadlessShop.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Catalog.Modules;

public static class SetupCatalogModule
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCatalogModule(string connectionString)
        {
            services.AddHeadlessDbContext<CatalogDbContext>(
                options =>
                    options.UseSqlite(
                        connectionString,
                        sqlite => sqlite.MigrationsHistoryTable("__CatalogMigrationsHistory")
                    ),
                headless => headless.RemoveSaveEntryProcessor<HeadlessLocalEventSaveEntryProcessor>()
            );
            services.AddScoped<IValidator<CreateProduct>, CreateProductValidator>();

            return services;
        }
    }
}
