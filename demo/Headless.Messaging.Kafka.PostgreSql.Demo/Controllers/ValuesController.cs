using System.Data;
using Dapper;
using Headless.Messaging;
using Headless.Messaging.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxQueue producer, IOutboxTransaction outboxTransaction) : Controller
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
        await producer.EnqueueAsync(
            new KafkaMessage(DateTime.UtcNow),
            new EnqueueOptions { MessageName = "sample.kafka.postgrsql", Delay = TimeSpan.FromSeconds(delaySeconds) }
        );

        return Ok();
    }

    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.EnqueueAsync(
            new KafkaMessage(DateTime.UtcNow),
            new EnqueueOptions { MessageName = "sample.kafka.postgrsql" }
        );

        return Ok();
    }

    [Route("~/adonet/transaction")]
    public async Task<IActionResult> AdonetWithTransaction()
    {
        await using (var connection = new NpgsqlConnection(AppConstants.DbConnectionString))
        {
            await using var transaction = await connection.BeginTransactionAsync(outboxTransaction, autoCommit: false);

            //your business code
            await connection.ExecuteAsync(
                "INSERT INTO \"Persons\"(\"Name\",\"Age\",\"CreateTime\") VALUES('Lucy',25, NOW())",
                transaction: (IDbTransaction?)transaction.DbTransaction
            );

            await producer.EnqueueAsync(
                new KafkaMessage(DateTime.UtcNow),
                new EnqueueOptions { MessageName = "sample.kafka.postgrsql" }
            );

            await transaction.CommitAsync();
        }

        await producer.EnqueueAsync(
            new KafkaMessage(DateTime.UtcNow),
            new EnqueueOptions { MessageName = "sample.kafka.postgrsql" }
        );

        return Ok();
    }

    [Route("~/ef/transaction")]
    public async Task<IActionResult> EntityFrameworkWithTransaction([FromServices] AppDbContext dbContext)
    {
        await using (await dbContext.Database.BeginTransactionAsync(outboxTransaction, autoCommit: false))
        {
            dbContext.Persons.Add(new Person { Name = "ef.transaction", Age = 11 });

            await dbContext.SaveChangesAsync();

            await producer.EnqueueAsync(
                new KafkaMessage(DateTime.UtcNow),
                new EnqueueOptions { MessageName = "sample.kafka.postgrsql" }
            );

            await dbContext.Database.CommitTransactionAsync();
        }
        return Ok();
    }
}

public record KafkaMessage(DateTime Value);

public sealed class KafkaMessageConsumer : IConsume<KafkaMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<KafkaMessage> context, CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $@"Subscriber output message: {context.Message.Value.ToString(CultureInfo.InvariantCulture)}"
        );
        return ValueTask.CompletedTask;
    }
}
