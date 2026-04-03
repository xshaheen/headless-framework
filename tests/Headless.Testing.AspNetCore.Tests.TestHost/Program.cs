// Copyright (c) Mahmoud Shaheen. All rights reserved.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

app.MapGet("/", () => "ok");
app.MapGet("/time", (TimeProvider tp) => tp.GetUtcNow().ToString("O"));

app.Run();
