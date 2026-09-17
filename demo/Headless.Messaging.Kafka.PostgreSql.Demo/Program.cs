using Demo;
using Demo.Controllers;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(AppConstants.DbConnectionString));

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.Queue.ForMessage<KafkaMessage>(message =>
        message
            .Contract("sample.kafka.postgrsql")
            .Consumer<KafkaMessageConsumer>(consumer => consumer.ConsumerIdentity("kafka-postgresql.message"))
    );

    //setup.UseEntityFramework<AppDbContext>();
    //docker run --name postgres -p 5432:5432 -e POSTGRES_PASSWORD=mysecretpassword -d postgres
    setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
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

// Declares the two unit-of-work providers this demo enlists through: the Npgsql helpers
// (BeginAsync(connection), Enlist(connection, transaction), RunAsync(connection, …)) for the raw-ADO capability,
// and the EF Core helpers (RunAsync(db, …)) for the EF capability. Both are idempotent thin wrappers over the
// scoped IUnitOfWorkManager that AddHeadlessMessaging already registers — no interceptor or hosted service needed;
// RunAsync/BeginAsync own the transaction's begin and commit directly.
builder.Services.AddPostgreSqlUnitOfWork();
builder.Services.AddEntityFrameworkUnitOfWork();

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
