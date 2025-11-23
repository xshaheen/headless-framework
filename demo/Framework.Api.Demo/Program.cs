// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api;
using Framework.Api.Demo;
using Framework.Api.Demo.Endpoints;
using Framework.Api.Middlewares;
using Framework.Api.MinimalApi;
using Framework.Api.Mvc;
using Framework.OpenApi.Nswag;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddFrameworkApiServices();
builder.Services.AddFrameworkNswagOpenApi();
builder.Services.AddFrameworkMvcOptions();
builder.Services.AddFrameworkMinimalApiOptions();
builder.Services.AddCustomStatusCodesRewriterMiddleware();
builder.Services.AddControllers();

// Add Basic authentication
builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Basic";
        options.DefaultChallengeScheme = "Basic";
    })
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", configureOptions: null);

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopmentOrTest())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

app.UseCustomStatusCodesRewriter();
app.UseAuthentication();
app.UseAuthorization();
app.MapFrameworkNswagOpenApi();
app.MapControllers();
app.MapProblemsEndpoints();

await app.RunAsync();
