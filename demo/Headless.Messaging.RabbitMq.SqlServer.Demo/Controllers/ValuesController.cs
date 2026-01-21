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
public class ValuesController(IOutboxPublisher publisher) : Controller
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
        await publisher.PublishAsync("sample.rabbitmq.sqlserver", new Person { Id = 123, Name = "Bar" });

        return Ok();
    }

    [Route("~/delay/{delaySeconds:int}")]
    public async Task<IActionResult> Delay(int delaySeconds)
    {
        await publisher.PublishDelayAsync(
            TimeSpan.FromSeconds(delaySeconds),
            "sample.rabbitmq.sqlserver",
            new Person { Id = 123, Name = "Bar" }
        );

        return Ok();
    }

    [Route("~/delay/transaction/{delaySeconds:int}")]
    public async Task<IActionResult> DelayWithTransaction(int delaySeconds)
    {
        await using (var connection = new SqlConnection(AppDbContext.ConnectionString))
        {
            using var transaction = await connection.BeginTransactionAsync(publisher, autoCommit: true);

            //your business code
            await connection.ExecuteAsync(
                "INSERT INTO Persons(Name,Age,CreateTime) VALUES(@Name,@Age, GETDATE())",
                new { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) },
                transaction: transaction
            );

            await publisher.PublishDelayAsync(
                TimeSpan.FromSeconds(delaySeconds),
                "sample.rabbitmq.sqlserver",
                new Person { Id = 123, Name = "Bar" }
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
            using var transaction = await connection.BeginTransactionAsync(publisher, autoCommit: false);

            await publisher.PublishAsync("sample.rabbitmq.sqlserver", person);

            await connection.ExecuteAsync(
                "INSERT INTO Persons(Name,Age,CreateTime) VALUES(@Name,@Age, GETDATE())",
                param: new { person.Name, person.Age },
                transaction: transaction
            );

            await publisher.PublishDelayAsync(TimeSpan.FromSeconds(5), "sample.rabbitmq.sqlserver", person);

            await ((DbTransaction)transaction).CommitAsync();
        }

        person.Name = new RealNameGenerator().Generate();

        await publisher.PublishAsync("sample.rabbitmq.sqlserver", person);

        return Ok();
    }

    [Route("~/ef/transaction")]
    public IActionResult EntityFrameworkWithTransaction([FromServices] AppDbContext dbContext)
    {
        using (dbContext.Database.BeginTransaction(publisher, autoCommit: true))
        {
            dbContext.Persons.Add(new Person { Name = "ef.transaction" });
            dbContext.SaveChanges();
            publisher.Publish("sample.rabbitmq.sqlserver", new Person { Id = 123, Name = "Bar" });
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
            using var transaction = await connection.BeginTransactionAsync(publisher);
            // This is where you would do other work that is going to persist data to your database

            var message = TestMessage.Create($"This is message text created at {DateTime.Now:O}.");

            await publisher.PublishAsync(typeof(TestMessage).FullName!, message);
            transaction.Commit();
        }

        return Content("ok");
    }
}

public sealed class PersonConsumer : IConsume<Person>
{
    public ValueTask Consume(ConsumeContext<Person> context, CancellationToken cancellationToken)
    {
        Console.WriteLine($@"{DateTime.Now} Subscriber invoked, Info: {context.Message}");
        return ValueTask.CompletedTask;
    }
}
