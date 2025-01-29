// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api;
using Framework.Api.Demo.Endpoints;
using Framework.Api.MinimalApi;
using Framework.Api.Mvc;
using Framework.OpenApi.Nswag;

var builder = WebApplication.CreateBuilder(args);

builder.AddFrameworkApiServices();
builder.Services.AddFrameworkNswagOpenApi();
builder.Services.AddFrameworkMvcOptions();
builder.Services.AddFrameworkMinimalApiOptions();
builder.Services.AddControllers();

var app = builder.Build();

app.MapFrameworkNswagOpenApi();
app.MapControllers();
app.MapProblemsEndpoints();

await app.RunAsync();
