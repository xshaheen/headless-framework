// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Demo;
using Demo.Endpoints;
using Headless.Api;
using Headless.Api.Middlewares;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessFramework().ConfigureMinimalApi();
builder.Services.AddNswagOpenApi();
builder.Services.ConfigureMvc();
builder.Services.AddStatusCodesRewriterMiddleware();
builder.Services.AddControllers();

// Add Basic authentication
builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Basic";
        options.DefaultChallengeScheme = "Basic";
        options.DefaultForbidScheme = "Basic";
    })
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", "Basic", configureOptions: null);

builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy(
        "NamePolicy",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(UserClaimTypes.Name, allowedValues: "admin");
        }
    );

var app = builder.Build();

// UseExceptionHandler runs in every environment so the framework's HeadlessApiExceptionHandler
// (auto-registered by AddHeadlessFramework()) maps framework-known exceptions to ProblemDetails consistently
// in Development and Production. UseDeveloperExceptionPage is intentionally not registered: it
// would intercept exceptions before the IExceptionHandler chain can see them and would break the
// API contract this demo is meant to showcase.
app.UseExceptionHandler();

app.UseStatusCodesRewriter();
app.UseAuthentication();
app.UseAuthorization();
app.MapNswagOpenApi();
app.MapControllers();
app.MapProblemsEndpoints();

await app.RunAsync();
