using Dapper;
using Demo;
using Headless.Messaging.Dashboard;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

// Configure services
//docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=yourStrong(!)Password" -e "MSSQL_PID=Evaluation" -p 1433:1433 \
// --name sqlpreview --hostname sqlpreview -d mcr.microsoft.com/mssql/server:2022-preview-ubuntu-22.04
builder.Services.AddDbContext<AppDbContext>();

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
    setup.SubscribeFromAssembly(typeof(Program).Assembly);

    setup.UseEntityFramework<AppDbContext>();
    setup.UseRabbitMq("127.0.0.1");
    setup.UseDashboard(d => d.WithNoAuth());

    //setup.Options.EnablePublishParallelSend = true;

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

builder.Services.AddControllers();

var app = builder.Build();

// Configure middleware pipeline
app.UseRouting();
app.MapControllers();
await app.RunAsync();
