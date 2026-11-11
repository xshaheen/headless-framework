// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Demo;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.LiveQueries;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddHeadless(configureServices: options =>
    {
        options.OpenTelemetry.Enabled = false;
        // The page is two files in wwwroot, served by the static file middleware below.
        options.StaticAssets.Enabled = false;
    })
    .ConfigureHeadlessMinimalApi();

// An in-memory database that lives as long as this connection, so the demo leaves no file behind. Every context
// opens a connection of its own to it, which is what lets concurrent requests hold concurrent transactions.
const string connectionString = "Data Source=live-queries-demo;Mode=Memory;Cache=Shared";
await using var keepAlive = new SqliteConnection(connectionString);
await keepAlive.OpenAsync();

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddEntityFrameworkUnitOfWork();
builder.Services.AddDemoMediator();

// The whole opt-in. UseEndpoints lets a GET endpoint stream; UseEntityFramework records what a live handler
// reads through the context and announces what a commit through it wrote. With row-level matching a saved row
// wakes only the streams whose filter it can pass. LiveQueries:RowLevelMatching=false shows the difference.
var rowLevelMatching = builder.Configuration.GetValue("LiveQueries:RowLevelMatching", defaultValue: true);

builder.Services.AddHeadlessLiveQueries(setup =>
    setup.UseEndpoints().UseEntityFramework<AppDbContext>(options => options.RowLevelMatching = rowLevelMatching)
);

builder.Services.AddSingleton<LiveQueriesMeterReader>();
builder.Services.AddHostedService<TodoStatsLogger>();

var app = builder.Build();

// Listening has to start before the first query runs, or the totals miss it.
var meter = app.Services.GetRequiredService<LiveQueriesMeterReader>();

// A real host creates its schema and seed data in a deploy step. This database is empty at every start.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    db.Counters.Add(new Counter { Id = 1 });
    db.Todos.Add(
        new Todo
        {
            Id = Guid.CreateVersion7(),
            Title = "Open this page in a second window",
            OwnerId = 1,
        }
    );
    db.Todos.Add(
        new Todo
        {
            Id = Guid.CreateVersion7(),
            Title = "Switch one window to another owner",
            OwnerId = 2,
        }
    );
    await db.SaveChangesAsync();
}

app.UseHeadless();

// Reads the session and command ids a client sends with a command, so the client's own streams can report the
// command. A host with authentication calls this after UseAuthentication(): the user is read here.
app.UseHeadlessLiveCommands();

app.UseDefaultFiles();
app.UseStaticFiles();

// Health checks and the OpenAPI document at /openapi/v1.json. Each live operation carries x-headless-live there,
// which is how the page and tools/gen-client.mjs find the routes below.
app.MapHeadlessEndpoints();

var todos = app.MapGroup("/todos").WithTags("Todos");

// Queries. An ordinary GET answers once; a GET that accepts a stream format stays open and re-sends on change.
todos
    .MapGetRequest<ListOwnerTodos, IReadOnlyList<TodoView>>("/owner/{ownerId:int}")
    .WithName(nameof(ListOwnerTodos))
    .WithLiveUpdates();
todos.MapGetRequest<GetTodoStats, TodoStats>("/stats").WithName(nameof(GetTodoStats)).WithLiveUpdates();

todos.MapPostRequest<AddTodo, TodoView>("/").WithName(nameof(AddTodo));
todos.MapPostRequest<ToggleTodo, TodoView>("/toggle").WithName(nameof(ToggleTodo));
todos.MapDeleteRequest<RemoveTodo>("/{id:guid}").WithName(nameof(RemoveTodo));

// An action: it commits nothing, so no stream reports it.
todos.MapPostRequest<SummarizeTodos, TodoSummary>("/summarize").WithName(nameof(SummarizeTodos));

var counter = app.MapGroup("/counter").WithTags("Counter");
counter.MapGetRequest<GetCounter, CounterView>("/").WithName(nameof(GetCounter)).WithLiveUpdates();
counter.MapPostRequest<IncrementCounter, CounterView>("/increment").WithName(nameof(IncrementCounter));

// Demo only: the meter's totals, for the page and tools/bench-invalidation.mjs.
app.MapGet(
        "/demo/meter",
        () =>
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["rowLevelMatching"] = rowLevelMatching,
                ["totals"] = meter.Read(),
            }
    )
    .ExcludeFromDescription();

await app.RunAsync();
