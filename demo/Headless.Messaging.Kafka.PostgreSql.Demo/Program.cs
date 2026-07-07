using Demo;
using Demo.Controllers;
using Headless.CommitCoordination.EntityFramework;
using Headless.CommitCoordination.PostgreSql;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Kafka;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddDbContext<AppDbContext>(
    // AddInterceptors wires the DI-registered commit-coordination EF interceptor into the context options —
    // EF Core does not auto-discover IInterceptor registrations, so the EF commit edge would otherwise go unobserved.
    (sp, opt) => opt.UseNpgsql(AppConstants.DbConnectionString).AddInterceptors(sp.GetServices<IInterceptor>())
);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.ForMessage<KafkaMessage>(message =>
        message.MessageName("sample.kafka.postgrsql").OnQueue<KafkaMessageConsumer>()
    );

    //setup.UseEntityFramework<AppDbContext>();
    //docker run --name postgres -p 5432:5432 -e POSTGRES_PASSWORD=mysecretpassword -d postgres
    setup.UsePostgreSql(AppConstants.DbConnectionString);

    /* //Run Kafka Docker Container (Powershell)
    docker run -d `
        --name kafka `
        -p 9092:9092 `
        -e KAFKA_NODE_ID=1 `
        -e KAFKA_PROCESS_ROLES=broker,controller `
        -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:9092,CONTROLLER://:9093 `
        -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://127.0.0.1:9092 `
        -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER `
        -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT `
        -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:9093 `
        -e KAFKA_LOG_DIRS=/var/lib/kafka/data `
        -e KAFKA_AUTO_CREATE_TOPICS_ENABLE=true `
        -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 `
        -e KAFKA_OFFSETS_TOPIC_MIN_ISR=1 `
        -e KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1 `
        -e KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1 `
        apache/kafka:3.7.0
    */
    setup.UseKafka("127.0.0.1:9092");
    setup.UseDashboard(d => d.WithNoAuth());
});

// Commit coordination — registered EXPLICITLY here because this demo uses the RAW PostgreSQL storage path
// (setup.UsePostgreSql(connString) above), not the EF-context path. On the EF-context path
// (setup.UseEntityFramework<TContext>(), as the SQL Server demo uses) the transactional outbox is ON BY DEFAULT and
// none of this is needed. PostgreSQL is an INLINE (caller-driven) signal source — Npgsql exposes no commit
// diagnostic — so raw enlistment must call SignalAsync(Committed) after committing. AddEntityFrameworkCommitCoordination
// registers the EF interceptor used by the DbContext-based helper (which signals on the EF commit edge for you).
builder.Services.AddPostgreSqlCommitCoordination();
builder.Services.AddEntityFrameworkCommitCoordination();

builder.Services.AddControllers();

var app = builder.Build();

// Create the demo's Persons table (the messaging outbox tables are managed separately by UsePostgreSql).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Configure middleware pipeline
app.UseRouting();
app.MapControllers();

await app.RunAsync();
