using Demo.Controllers;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Redis;
using Headless.Messaging.Storage.SqlServer;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.ForMessage<Person>(message => message.MessageName("test-message").OnQueue<PersonConsumer>());

    setup.UseRedis(redis =>
    {
        redis.Configuration = ConfigurationOptions.Parse("redis-node-0:6379,password=headless");
        redis.OnConsumeError = context => throw new InvalidOperationException("Redis consume error", context.Exception);
    });

    setup.UseSqlServer("Server=db;Database=master;User=sa;Password=P@ssw0rd;Encrypt=False");

    setup.UseDashboard(d => d.WithNoAuth());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
