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

    public static MessagingOptions AddOrderingMessaging(this MessagingOptions options)
    {
        options.WithTopicMapping<ProductCreated>("catalog.product-created");
        options.Subscribe<ProductCreatedConsumer>("catalog.product-created").Group("ordering");

        return options;
    }

    public static IEndpointRouteBuilder MapOrderingModule(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapOrderingEndpoints();
    }
}
