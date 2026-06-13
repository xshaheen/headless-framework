using Dapper;
using Demo;
using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Headless.CommitCoordination.SqlServer;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Configure services
//docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=yourStrong(!)Password" -e "MSSQL_PID=Evaluation" -p 1433:1433 \
// --name sqlpreview --hostname sqlpreview -d mcr.microsoft.com/mssql/server:2022-preview-ubuntu-22.04
builder.Services.AddDbContext<AppDbContext>(
    // AddInterceptors wires the DI-registered commit-coordination EF interceptor into the context options —
    // EF Core does not auto-discover IInterceptor registrations, so the EF commit edge would otherwise go unobserved.
    (sp, opt) => opt.UseSqlServer(AppDbContext.ConnectionString).AddInterceptors(sp.GetServices<IInterceptor>())
);

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

// Commit coordination: makes "write to the DB and publish in one transaction" atomic. SqlServer uses out-of-band
// commit detection (a SqlClient diagnostic listener, started by a hosted service), so no manual signal is needed.
// AddEntityFrameworkCommitCoordination registers the EF interceptor used by the DbContext-based helper.
builder.Services.AddSqlServerCommitCoordination();
builder.Services.AddEntityFrameworkCommitCoordination();

builder.Services.AddControllers();

var app = builder.Build();

// Configure middleware pipeline
app.UseRouting();
app.MapControllers();
await app.RunAsync();
