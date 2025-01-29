// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api;
using Framework.Api.Abstractions;
using Framework.Api.Mvc;
using Framework.OpenApi.Nswag;

var builder = WebApplication.CreateBuilder(args);

builder.AddFrameworkApiServices();
builder.Services.AddFrameworkNswagOpenApi();
builder.Services.AddFrameworkMvcOptions();
builder.Services.AddControllers();

var app = builder.Build();

app.MapFrameworkNswagOpenApi();
app.MapControllers();

app.Map(
    "minimal/malformed-syntax",
    (IProblemDetailsCreator factory, HttpContext context) => Results.Problem(factory.MalformedSyntax(context))
);

await app.RunAsync();
