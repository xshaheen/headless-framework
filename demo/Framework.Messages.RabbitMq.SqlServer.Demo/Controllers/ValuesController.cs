using System.Data.Common;
using Dapper;
using Demo.Messages;
using Framework.Messages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using NameGenerator.Generators;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxPublisher capPublisher) : Controller
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
        await capPublisher.PublishAsync("sample.rabbitmq.sqlserver", new Person { Id = 123, Name = "Bar" });

        return Ok();
    }

    [Route("~/delay/{delaySeconds:int}")]
    public async Task<IActionResult> Delay(int delaySeconds)
    {
        await capPublisher.PublishDelayAsync(
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
            using var transaction = await connection.BeginTransactionAsync(capPublisher, autoCommit: true);

            //your business code
            await connection.ExecuteAsync(
                "INSERT INTO Persons(Name,Age,CreateTime) VALUES(@Name,@Age, GETDATE())",
                new { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) },
                transaction: transaction
            );

            await capPublisher.PublishDelayAsync(
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
            using var transaction = await connection.BeginTransactionAsync(capPublisher, autoCommit: false);

            await capPublisher.PublishAsync("sample.rabbitmq.sqlserver", person);

            await connection.ExecuteAsync(
                "INSERT INTO Persons(Name,Age,CreateTime) VALUES(@Name,@Age, GETDATE())",
                param: new { person.Name, person.Age },
                transaction: transaction
            );

            await capPublisher.PublishDelayAsync(TimeSpan.FromSeconds(5), "sample.rabbitmq.sqlserver", person);

            await ((DbTransaction)transaction).CommitAsync();
        }

        person.Name = new RealNameGenerator().Generate();

        await capPublisher.PublishAsync("sample.rabbitmq.sqlserver", person);

        return Ok();
    }

    [Route("~/ef/transaction")]
    public IActionResult EntityFrameworkWithTransaction([FromServices] AppDbContext dbContext)
    {
        using (dbContext.Database.BeginTransaction(capPublisher, autoCommit: true))
        {
            dbContext.Persons.Add(new Person { Name = "ef.transaction" });
            dbContext.SaveChanges();
            capPublisher.Publish("sample.rabbitmq.sqlserver", new Person { Id = 123, Name = "Bar" });
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
            using var transaction = await connection.BeginTransactionAsync(capPublisher);
            // This is where you would do other work that is going to persist data to your database

            var message = TestMessage.Create($"This is message text created at {DateTime.Now:O}.");

            await capPublisher.PublishAsync(typeof(TestMessage).FullName!, message);
            transaction.Commit();
        }

        return Content("ok");
    }

    [NonAction]
    [CapSubscribe("sample.rabbitmq.sqlserver")]
    public void Subscriber(Person p)
    {
        Console.WriteLine($@"{DateTime.Now} Subscriber invoked, Info: {p}");
    }
}
