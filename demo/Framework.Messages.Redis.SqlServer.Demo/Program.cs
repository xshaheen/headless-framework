using Demo.Controllers;
using Framework.Messages;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMessages(x =>
{
    x.ScanConsumers(typeof(Program).Assembly);
    x.WithTopicMapping<Person>("test-message");

    x.UseRedis(redis =>
    {
        redis.Configuration = ConfigurationOptions.Parse("redis-node-0:6379,password=cap");
        redis.OnConsumeError = context => throw new InvalidOperationException("Redis consume error", context.Exception);
    });

    x.UseSqlServer("Server=db;Database=master;User=sa;Password=P@ssw0rd;Encrypt=False");

    x.UseDashboard();
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

app.Run();
