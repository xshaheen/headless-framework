// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Api.OperationProcessors;
using Headless.EntityFramework;
using Headless.Mediator;
using Headless.Messaging;
using Headless.MultiTenancy;
using HeadlessShop.Api;
using HeadlessShop.Catalog.Api;
using HeadlessShop.Modules;
using HeadlessShop.Ordering.Modules;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Shop") ?? "Data Source=headless-shop.db";

builder.AddHeadless(
    encryption =>
    {
        encryption.DefaultPassPhrase = _GetRequiredShopSetting(
            builder,
            "Encryption:DefaultPassPhrase",
            "headless-shop-local-dev-passphrase"
        );
        encryption.DefaultSalt = Encoding.UTF8.GetBytes(
            _GetRequiredShopSetting(builder, "Encryption:DefaultSalt", "headless-shop-local-dev-salt")
        );
    },
    hash =>
    {
        hash.DefaultSalt = _GetRequiredShopSetting(builder, "Hashing:DefaultSalt", "headless-shop-local-dev-hash-salt");
    },
    configureServices: options =>
    {
        // This tour ships its own OpenAPI via NSwag + Scalar (mapped in Development only), so disable the
        // built-in ServiceDefaults OpenAPI document to avoid a duplicate /openapi route mapped in every environment.
        options.OpenApi.Enabled = false;
    }
);

builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims())
        .Authorization(authorization => authorization.RequireTenant())
        .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
        .EntityFramework(entityFramework => entityFramework.GuardTenantWrites())
);

builder
    .Services.AddAuthentication(FakeTourAuthenticationHandler.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, FakeTourAuthenticationHandler>(
        FakeTourAuthenticationHandler.AuthenticationScheme,
        _ => { }
    );

builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy(
        CatalogEndpoints.CreateProductPermission,
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permission", CatalogEndpoints.CreateProductPermission);
        }
    )
    .SetDefaultPolicy(
        new AuthorizationPolicyBuilder(FakeTourAuthenticationHandler.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new TenantRequirement())
            .Build()
    );

builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddMediatorValidationRequestBehavior();
builder.Services.AddMediatorLoggingBehaviors();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddNswagOpenApi(setupGeneratorActions: settings =>
{
    settings.UseRouteNameAsOperationId = false;

    var extraInfoProcessor = settings.OperationProcessors.FirstOrDefault(processor =>
        processor is ApiExtraInformationOperationProcessor
    );

    if (extraInfoProcessor is not null)
    {
        settings.OperationProcessors.Remove(extraInfoProcessor);
    }
});
builder.Services.AddShopModules(connectionString);
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory();
    setup.UseInMemoryStorage();
    setup.AddOrderingMessaging();
});

var app = builder.Build();

await app.Services.InitializeShopDatabaseAsync();

app.UseHeadless();
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();

app.MapHeadlessEndpoints();
app.MapShopModules();

if (app.Environment.IsDevelopment())
{
    app.MapNswagOpenApi();
    app.MapScalarOpenApi();
}

await app.RunAsync();

static string _GetRequiredShopSetting(WebApplicationBuilder builder, string key, string developmentValue)
{
    var configurationKey = $"HeadlessShop:{key}";
    var configuredValue = builder.Configuration[configurationKey];

    if (!string.IsNullOrWhiteSpace(configuredValue))
    {
        return configuredValue;
    }

    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
    {
        return developmentValue;
    }

    throw new InvalidOperationException($"{configurationKey} must be configured outside Development.");
}

public partial class Program;
