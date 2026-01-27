using System.Data;
using Dapper;
using Headless.Messaging;
using Headless.Messaging.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxPublisher producer) : Controller
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

    [Route("~/delay/{delaySeconds:int}")]
    public async Task<IActionResult> Delay(int delaySeconds)
    {
        await producer.PublishDelayAsync(TimeSpan.FromSeconds(delaySeconds), "sample.kafka.postgrsql", DateTime.Now);

        return Ok();
    }

    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.PublishAsync("sample.kafka.postgrsql", DateTime.Now);

        return Ok();
    }

    [Route("~/adonet/transaction")]
    public async Task<IActionResult> AdonetWithTransaction()
    {
        using (var connection = new NpgsqlConnection(AppConstants.DbConnectionString))
        {
            using var transaction = connection.BeginTransaction(producer, autoCommit: false);

            //your business code
            connection.Execute(
                "INSERT INTO \"Persons\"(\"Name\",\"Age\",\"CreateTime\") VALUES('Lucy',25, NOW())",
                transaction: (IDbTransaction?)transaction.DbTransaction
            );

            await producer.PublishAsync("sample.kafka.postgrsql", DateTime.Now);

            transaction.Commit();
        }

        await producer.PublishAsync("sample.kafka.postgrsql", DateTime.Now);

        return Ok();
    }

    [Route("~/ef/transaction")]
    public async Task<IActionResult> EntityFrameworkWithTransaction([FromServices] AppDbContext dbContext)
    {
        using (dbContext.Database.BeginTransaction(producer, autoCommit: false))
        {
            dbContext.Persons.Add(new Person { Name = "ef.transaction", Age = 11 });

            dbContext.SaveChanges();

            await producer.PublishAsync("sample.kafka.postgrsql", DateTime.UtcNow);

            dbContext.Database.CommitTransaction();
        }
        return Ok();
    }
}

public record KafkaMessage(DateTime Value);

public sealed class KafkaMessageConsumer : IConsume<KafkaMessage>
{
    public ValueTask Consume(ConsumeContext<KafkaMessage> context, CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $@"Subscriber output message: {context.Message.Value.ToString(CultureInfo.InvariantCulture)}"
        );
        return ValueTask.CompletedTask;
    }
}
