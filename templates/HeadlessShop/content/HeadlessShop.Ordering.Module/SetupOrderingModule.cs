using FluentValidation;
using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Processors;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using HeadlessShop.Contracts;
using HeadlessShop.Ordering.Application;
using HeadlessShop.Ordering.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Ordering.Modules;

public static class SetupOrderingModule
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddOrderingModule(string connectionString)
        {
            services.AddHeadlessDbContext<OrderingDbContext>(
                options =>
                    options.UseNpgsql(
                        connectionString,
                        postgres => postgres.MigrationsHistoryTable("__OrderingMigrationsHistory", "ordering")
                    ),
                headless => headless.RemoveSaveEntryProcessor<HeadlessLocalEventSaveEntryProcessor>()
            );
            services.AddScoped<IValidator<PlaceOrder>, PlaceOrderValidator>();
            services.AddSingleton(TimeProvider.System);

            return services;
        }
    }
}

public static class SetupOrderingMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        public MessagingSetupBuilder AddOrderingMessaging()
        {
            setup.ForMessage<ProductCreated>(message =>
                message
                    .MessageName("catalog.product-created")
                    .OnBus<ProductCreatedConsumer>(consumer => consumer.Group("ordering"))
            );

            return setup;
        }
    }
}
