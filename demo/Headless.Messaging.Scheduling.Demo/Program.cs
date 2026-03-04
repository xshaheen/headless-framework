using Demo;
using Demo.Consumers;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Scheduling;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Minimal API + Swagger for quick demo exploration.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMessages(options =>
{
    // Register messaging consumers and scheduled jobs from this assembly.
    options.ScanConsumers(typeof(Program).Assembly);

    // Topic mapping for typed publishers.
    options.WithTopicMapping<PingMessage>("demo.ping");
    options.WithTopicMapping<WorkItemMessage>("demo.work");
    options.WithTopicMapping<HeartbeatMessage>("demo.heartbeat");

    // Explicit topic subscriptions for in-memory transport.
    options.Consumer<PingConsumer>().Topic("demo.ping").Build();
    options.Consumer<WorkItemConsumer>().Topic("demo.work").Build();
    options.Consumer<HeartbeatConsumer>().Topic("demo.heartbeat").Build();

    // In-memory transport + storage: fast, local, ephemeral.
    options.UseInMemoryStorage();
    options.UseInMemoryMessageQueue();

    // ScheduledTrigger consumers: assign distinct groups to avoid duplicate subscriber warnings.
    options.Consumer<HeartbeatJob>().Group("demo.scheduling.heartbeat").Build();
    options.Consumer<OneTimeJobConsumer>().Group("demo.scheduling.onetime").Build();

    // Fluent scheduled job (in addition to [Recurring] attribute).
    options
        .Consumer<FluentScheduledJob>()
        .Group("demo.scheduling.fluent")
        .WithSchedule("*/15 * * * * *")
        .WithTimeZone("UTC")
        .Build();

    // Dashboard for messaging + scheduling observability.
    options.UseDashboard(dashboard =>
    {
        dashboard.PathMatch = "/messaging";
        dashboard.StatsPollingInterval = 1000;
        dashboard.AllowAnonymousExplicit = true;
    });
});

// Scheduler tuning for snappy demo behavior.
builder.Services.Configure<SchedulerOptions>(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.MaxPollingInterval = TimeSpan.FromSeconds(5);
    options.BatchSize = 10;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.MapDemoEndpoints();
app.Run();
