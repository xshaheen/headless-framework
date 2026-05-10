using System.Data.Common;
using Dapper;
using Demo.Messages;
using Headless.Messaging;
using Headless.Messaging.SqlServer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using NameGenerator.Generators;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(
    IOutboxPublisher producer,
    IScheduledPublisher scheduler,
    IOutboxTransaction outboxTransaction
) : Controller
{
    [Route("~/control/start")]
    public async Task<IActionResult> Start([FromServices] IBootstrapper bootstrapper)
    {
        await bootstrapper.BootstrapAsync();
        return Ok();
    }

    [Route("~/control/stop")]
    public async Task<IActionResult> Stop([FromServices] IBootstrapper bootstrapper)
    {
        await bootstrapper.DisposeAsync();
        return Ok();
    }

    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.PublishAsync(
            new Person { Id = 123, Name = "Bar" },
            new PublishOptions { Topic = "sample.rabbitmq.sqlserver" }
        );

        return Ok();
    }

    [Route("~/delay/{delaySeconds:int}")]
    public async Task<IActionResult> Delay(int delaySeconds)
    {
        await scheduler.PublishDelayAsync(
            TimeSpan.FromSeconds(delaySeconds),
            new Person { Id = 123, Name = "Bar" },
            new PublishOptions { Topic = "sample.rabbitmq.sqlserver" }
        );

        return Ok();
    }

    [Route("~/delay/transaction/{delaySeconds:int}")]
    public async Task<IActionResult> DelayWithTransaction(int delaySeconds)
    {
        await using (var connection = new SqlConnection(AppDbContext.ConnectionString))
        {
            await using var transaction = await connection.BeginTransactionAsync(outboxTransaction, autoCommit: true);

            //your business code
            await connection.ExecuteAsync(
                "INSERT INTO Persons(Name,Age,CreateTime) VALUES(@Name,@Age, GETDATE())",
                new { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) },
                transaction: (DbTransaction?)transaction.DbTransaction
            );

            await scheduler.PublishDelayAsync(
                TimeSpan.FromSeconds(delaySeconds),
                new Person { Id = 123, Name = "Bar" },
                new PublishOptions { Topic = "sample.rabbitmq.sqlserver" }
            );
        }

        return Ok();
    }

    [Route("~/adonet/transaction")]
    public async Task<IActionResult> AdonetWithTransaction()
    {
        var person = new Person { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) };

        await using (var connection = new SqlConnection(AppDbContext.ConnectionString))
        {
            await using var transaction = await connection.BeginTransactionAsync(outboxTransaction, autoCommit: false);

            await producer.PublishAsync(person, new PublishOptions { Topic = "sample.rabbitmq.sqlserver" });

            await connection.ExecuteAsync(
                "INSERT INTO Persons(Name,Age,CreateTime) VALUES(@Name,@Age, GETDATE())",
                param: new { person.Name, person.Age },
                transaction: (DbTransaction?)transaction.DbTransaction
            );

            await scheduler.PublishDelayAsync(
                TimeSpan.FromSeconds(5),
                person,
                new PublishOptions { Topic = "sample.rabbitmq.sqlserver" }
            );

            await ((DbTransaction)transaction.DbTransaction!).CommitAsync();
        }

        person.Name = new RealNameGenerator().Generate();

        await producer.PublishAsync(person, new PublishOptions { Topic = "sample.rabbitmq.sqlserver" });

        return Ok();
    }

    [Route("~/ef/transaction")]
    public async Task<IActionResult> EntityFrameworkWithTransaction([FromServices] AppDbContext dbContext)
    {
        await using (await dbContext.Database.BeginTransactionAsync(outboxTransaction, autoCommit: true))
        {
            dbContext.Persons.Add(new Person { Name = "ef.transaction" });
            await dbContext.SaveChangesAsync();
            await producer.PublishAsync(
                new Person { Id = 123, Name = "Bar" },
                new PublishOptions { Topic = "sample.rabbitmq.sqlserver" }
            );
        }
        return Ok();
    }

    [Route("~/typed/subscribe")]
    public async Task<IActionResult> TypePublish()
    {
        // Add the following code to startup.cs
        //services
        //    .AddSingleton<IConsumerServiceSelector, TypedConsumerServiceSelector>()
        //    .AddQueueHandlers(typeof(Startup).Assembly);

        await using (var connection = new SqlConnection(AppDbContext.ConnectionString))
        {
            await using var transaction = await connection.BeginTransactionAsync(outboxTransaction);
            // This is where you would do other work that is going to persist data to your database

            var message = TestMessage.Create($"This is message text created at {DateTime.UtcNow:O}.");

            await producer.PublishAsync(message, new PublishOptions { Topic = typeof(TestMessage).FullName! });
            await transaction.CommitAsync();
        }

        return Content("ok");
    }
}

public sealed class PersonConsumer : IConsume<Person>
{
    public ValueTask Consume(ConsumeContext<Person> context, CancellationToken cancellationToken)
    {
        Console.WriteLine($@"{DateTime.UtcNow} Subscriber invoked, Info: {context.Message}");
        return ValueTask.CompletedTask;
    }
}
