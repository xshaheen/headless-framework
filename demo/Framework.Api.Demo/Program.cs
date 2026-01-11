// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api;
using Framework.Api.Demo;
using Framework.Api.Demo.Endpoints;
using Framework.Api.Middlewares;
using Framework.Constants;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessApi(encryption =>
{
    encryption.DefaultPassPhrase = "DemoPassPhrase123456";
    encryption.InitVectorBytes = "DemoIV0123456789"u8.ToArray();
    encryption.DefaultSalt = "DemoSalt"u8.ToArray();
}).ConfigureHeadlessMinimalApi();
builder.Services.AddHeadlessNswagOpenApi();
builder.Services.ConfigureHeadlessMvc();
builder.Services.AddHeadlessStatusCodesRewriterMiddleware();
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
else
{
    app.UseExceptionHandler();
}

app.UseHeadlessStatusCodesRewriter();
app.UseAuthentication();
app.UseAuthorization();
app.MapFrameworkNswagOpenApi();
app.MapControllers();
app.MapProblemsEndpoints();

await app.RunAsync();
