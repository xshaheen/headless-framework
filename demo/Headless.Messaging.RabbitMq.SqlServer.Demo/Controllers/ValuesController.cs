using Dapper;
using Headless.CommitCoordination.SqlServer;
using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NameGenerator.Generators;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxBus producer, IServiceProvider services) : Controller
{
    private const string _MessageName = "sample.rabbitmq.sqlserver";

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

    // Baseline: publish with no surrounding transaction. The message is stored and dispatched immediately, NOT
    // atomic with any database write. Contrast with the coordinated endpoints below.
    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.PublishAsync(
            new Person { Name = "Bar", Age = 42 },
            new PublishOptions { MessageName = _MessageName }
        );

        return Ok();
    }

    [Route("~/delay/{delaySeconds:int}")]
    public async Task<IActionResult> Delay(int delaySeconds)
    {
        await producer.PublishAsync(
            new Person { Name = "Bar", Age = 42 },
            new PublishOptions { MessageName = _MessageName, Delay = TimeSpan.FromSeconds(delaySeconds) }
        );

        return Ok();
    }

    // CAPABILITY 1 — raw ADO (Dapper) coordinated transaction via the EnlistCommitCoordination advanced seam.
    // The caller owns the transaction (so it can pass it to Dapper) and enlists it synchronously, which makes the
    // coordinator ambient. The PublishAsync then writes its outbox row in the SAME transaction. On SqlServer commit
    // detection is OUT-OF-BAND (a SqlClient diagnostic listener), so committing the transaction is enough — no
    // manual signal is needed (contrast with PostgreSQL, which requires an explicit SignalAsync).
    [Route("~/coordinated/adonet")]
    public async Task<IActionResult> CoordinatedAdoNet()
    {
        var person = new Person { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) };
        var ct = HttpContext.RequestAborted;

        await using var connection = new SqlConnection(AppDbContext.ConnectionString);
        await connection.OpenAsync(ct);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        await using (transaction)
        // Enlist synchronously in this frame so the ambient coordinator flows to the publish below.
        using (connection.EnlistCommitCoordination(transaction, services))
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO Persons(Name, Age, CreateTime) VALUES(@Name, @Age, GETDATE())",
                    new { person.Name, person.Age },
                    transaction,
                    cancellationToken: ct
                )
            );

            await producer.PublishAsync(person, new PublishOptions { MessageName = _MessageName }, ct);

            await transaction.CommitAsync(ct); // the out-of-band diagnostic observer drains the publish on commit
        }

        return Ok($"Inserted {person} and published atomically (raw ADO; out-of-band commit detection).");
    }

    // CAPABILITY 2 — EF Core coordinated transaction.
    // The DbContext helper runs inside EF's execution strategy (retry-safe), opens + enlists + commits in one call.
    // SaveChanges and the publish commit together; a retried attempt discards its buffer and re-runs cleanly.
    [Route("~/coordinated/ef")]
    public async Task<IActionResult> CoordinatedEntityFramework([FromServices] AppDbContext dbContext)
    {
        var person = new Person { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) };

        await dbContext.ExecuteCoordinatedTransactionAsync(
            async (ctx, ct) =>
            {
                ((AppDbContext)ctx).Persons.Add(person);
                await ctx.SaveChangesAsync(ct);

                await producer.PublishAsync(person, new PublishOptions { MessageName = _MessageName }, ct);
            },
            services,
            cancellationToken: HttpContext.RequestAborted
        );

        return Ok($"Inserted {person} and published atomically (EF Core).");
    }

    // CAPABILITY 3 — rollback discards the publish (the core invariant).
    // The INSERT and the publish are buffered, then the operation throws. The transaction rolls back, so the row is
    // never persisted AND the message is never dispatched — no half-completed unit of work.
    [Route("~/coordinated/rollback")]
    public async Task<IActionResult> CoordinatedRollback([FromServices] AppDbContext dbContext)
    {
        var person = new Person { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) };

        try
        {
            await dbContext.ExecuteCoordinatedTransactionAsync(
                async (ctx, ct) =>
                {
                    ((AppDbContext)ctx).Persons.Add(person);
                    await ctx.SaveChangesAsync(ct);

                    await producer.PublishAsync(person, new PublishOptions { MessageName = _MessageName }, ct);

                    throw new InvalidOperationException("Simulated failure after the buffered publish.");
                },
                services,
                cancellationToken: HttpContext.RequestAborted
            );
        }
        catch (InvalidOperationException)
        {
            return Ok("Transaction rolled back: neither the row nor the message survived (discard-on-rollback).");
        }

        return Ok();
    }

    // CAPABILITY 4 — delayed publish inside a coordinated transaction.
    // The delayed message is still bound to the commit: it is only scheduled if the transaction commits.
    [Route("~/coordinated/delay/{delaySeconds:int}")]
    public async Task<IActionResult> CoordinatedDelay(int delaySeconds, [FromServices] AppDbContext dbContext)
    {
        var person = new Person { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) };

        await dbContext.ExecuteCoordinatedTransactionAsync(
            async (ctx, ct) =>
            {
                ((AppDbContext)ctx).Persons.Add(person);
                await ctx.SaveChangesAsync(ct);

                await producer.PublishAsync(
                    person,
                    new PublishOptions { MessageName = _MessageName, Delay = TimeSpan.FromSeconds(delaySeconds) },
                    ct
                );
            },
            services,
            cancellationToken: HttpContext.RequestAborted
        );

        return Ok($"Inserted {person}; delayed publish ({delaySeconds}s) bound to the commit.");
    }
}

public sealed class PersonConsumer : IConsume<Person>
{
    public ValueTask ConsumeAsync(ConsumeContext<Person> context, CancellationToken cancellationToken)
    {
        Console.WriteLine($@"{DateTime.UtcNow} Subscriber invoked, Info: {context.Message}");
        return ValueTask.CompletedTask;
    }
}
