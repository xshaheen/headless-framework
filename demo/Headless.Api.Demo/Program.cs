// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Demo;
using Demo.Endpoints;
using Headless.Api;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddHeadless(configureServices: options =>
    {
        options.Validation.ValidateServiceProviderOnStartup = false;
        options.Validation.RequireUseHeadless = false;
        options.Validation.RequireMapHeadlessEndpoints = false;
        options.OpenTelemetry.Enabled = false;
        options.OpenApi.Enabled = false;
    })
    .ConfigureMinimalApi();
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

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
app.UseExceptionHandler();

app.UseStatusCodesRewriter();
app.UseAuthentication();
app.UseAuthorization();
app.MapNswagOpenApi();
app.MapControllers();
app.MapProblemsEndpoints();

await app.RunAsync();
