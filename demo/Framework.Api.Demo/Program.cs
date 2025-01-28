// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api;
using Framework.OpenApi.Nswag;

var builder = WebApplication.CreateBuilder(args);

builder.AddFrameworkApiServices();
builder.Services.AddFrameworkNswagOpenApi();
builder.Services.AddControllers();

var app = builder.Build();

app.MapFrameworkNswagOpenApi();
app.MapControllers();

await app.RunAsync();
