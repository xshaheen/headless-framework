// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Processors;
using Headless.Messaging.Configuration;
using HeadlessShop.Contracts;
using HeadlessShop.Ordering.Api;
using HeadlessShop.Ordering.Application;
using HeadlessShop.Ordering.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Ordering.Modules;

public static class OrderingModule
{
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, string connectionString)
    {
        services.AddHeadlessDbContext<OrderingDbContext>(
            options => options.UseSqlite(connectionString),
            headless => headless.RemoveSaveEntryProcessor<HeadlessLocalEventSaveEntryProcessor>()
        );
        services.AddScoped<IValidator<PlaceOrderCommand>, PlaceOrderCommandValidator>();

        return services;
    }

    public static MessagingSetupBuilder AddOrderingMessaging(this MessagingSetupBuilder setup)
    {
        setup.ForMessage<ProductCreated>(message =>
            message
                .MessageName("catalog.product-created")
                .OnBus<ProductCreatedConsumer>(consumer => consumer.Group("ordering"))
        );

        return setup;
    }

    public static IEndpointRouteBuilder MapOrderingModule(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapOrderingEndpoints();
    }
}
