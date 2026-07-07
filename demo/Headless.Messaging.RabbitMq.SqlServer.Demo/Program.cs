using Dapper;
using Demo;
using Headless.CommitCoordination;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure services
//docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=yourStrong(!)Password" -e "MSSQL_PID=Evaluation" -p 1433:1433 \
// --name sqlpreview --hostname sqlpreview -d mcr.microsoft.com/mssql/server:2022-preview-ubuntu-22.04
// Plain AddDbContext — no AddInterceptors needed: because messaging uses the EF storage path
// (setup.UseEntityFramework<AppDbContext>() below), the transactional outbox is ON BY DEFAULT and the
// commit-coordination interceptor is auto-attached to this context's options.
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlServer(AppDbContext.ConnectionString));

//builder.Services
//    .AddSingleton<IConsumerServiceSelector, TypedConsumerServiceSelector>()
//    .AddQueueHandlers(typeof(Program).Assembly);

// Initialize database schema
await using (var connection = new SqlConnection(AppDbContext.ConnectionString))
{
    await connection.ExecuteAsync(
        """
        IF EXISTS (SELECT * FROM sys.all_objects WHERE object_id = OBJECT_ID(N'[dbo].[Persons]') AND type IN ('U'))
        	DROP TABLE [dbo].[Persons]

        CREATE TABLE [dbo].[Persons] (
          [Id] int  IDENTITY(1,1) NOT NULL,
          [Name] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
          [Age] int  NULL,
          [CreateTime] datetime2(7) DEFAULT getdate() NULL
        )
        """
    );
}

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.ForMessagesFromAssembly(typeof(Program).Assembly);
    setup.UseEntityFramework<AppDbContext>();
    setup.UseRabbitMq("127.0.0.1");
    setup.UseDashboard(d => d.WithNoAuth());

    //setup.Options.EnablePublishParallelSend = true;
    // (commit-coordination registration follows AddHeadlessMessaging, below)

    //setup.Options.RetryPolicy.OnExhausted = (failed, ct) =>
    //{
    //    var logger = failed.ServiceProvider.GetRequiredService<ILogger<Program>>();
    //    logger.LogError($@"A message of type {failed.MessageType} failed after consuming the retry budget
    //        (MaxInlineRetries={setup.Options.RetryPolicy.MaxInlineRetries}, MaxPersistedRetries={setup.Options.RetryPolicy.MaxPersistedRetries}),
    //        requiring manual troubleshooting. Message name: {failed.Message.GetName()}");
    //    return Task.CompletedTask;
    //};
    //setup.Options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
});

// The EF-storage path already enabled the transactional outbox (commit coordination + EF interceptor) by default,
// so the /coordinated/ef, /coordinated/rollback, and /coordinated/delay endpoints need no extra wiring. This single
// call is only for the raw-ADO /coordinated/adonet endpoint, which enlists a raw SqlConnection via the SqlServer
// out-of-band diagnostic signal source.
builder.Services.AddSqlServerCommitCoordination();

builder.Services.AddControllers();

var app = builder.Build();

// Configure middleware pipeline
app.UseRouting();
app.MapControllers();
await app.RunAsync();
