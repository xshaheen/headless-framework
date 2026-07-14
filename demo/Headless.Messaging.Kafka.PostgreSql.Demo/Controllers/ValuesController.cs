using Dapper;
using Headless.CommitCoordination;
using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxQueue producer, IServiceProvider services) : Controller
{
    private const string _MessageName = "sample.kafka.postgrsql";

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

    // Baseline: enqueue with no surrounding transaction — stored and dispatched immediately, not atomic with any
    // database write. Contrast with the coordinated endpoints below.
    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.EnqueueAsync(
            new KafkaMessage(DateTime.UtcNow),
            new EnqueueOptions { MessageName = _MessageName }
        );

        return Ok();
    }

    [Route("~/delay/{delaySeconds:int}")]
    public async Task<IActionResult> Delay(int delaySeconds)
    {
        await producer.EnqueueAsync(
            new KafkaMessage(DateTime.UtcNow),
            new EnqueueOptions { MessageName = _MessageName, Delay = TimeSpan.FromSeconds(delaySeconds) }
        );

        return Ok();
    }

    // CAPABILITY 1 — raw ADO (Dapper) coordinated transaction via the EnlistCommitCoordination advanced seam.
    // The caller owns the transaction (so it can pass it to Dapper) and enlists it synchronously, which makes the
    // coordinator ambient. The EnqueueAsync then writes its outbox row in the SAME transaction. PostgreSQL is an
    // INLINE signal source (no commit diagnostic), so after committing the caller MUST call
    // scope.SignalAsync(Committed) — otherwise the un-signalled scope dispose discards the enqueued work.
    [Route("~/coordinated/adonet")]
    public async Task<IActionResult> CoordinatedAdoNet()
    {
        var person = new Person
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"adonet-{Random.Shared.Next(1000, 9999)}"),
            Age = Random.Shared.Next(10, 99),
        };
        var ct = HttpContext.RequestAborted;

        await using var connection = new NpgsqlConnection(AppConstants.DbConnectionString);
        await connection.OpenAsync(ct);
        var transaction = await connection.BeginTransactionAsync(ct);

        await using (transaction)
        // Enlist synchronously in this frame so the ambient coordinator flows to the enqueue below.
        await using (var scope = connection.EnlistCommitCoordination(transaction, services))
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """INSERT INTO "Persons"("Name", "Age") VALUES(@Name, @Age)""",
                    new { person.Name, person.Age },
                    transaction,
                    cancellationToken: ct
                )
            );

            await producer.EnqueueAsync(
                new KafkaMessage(DateTime.UtcNow),
                new EnqueueOptions { MessageName = _MessageName },
                ct
            );

            await transaction.CommitAsync(ct);
            await scope.SignalAsync(CommitOutcome.Committed); // REQUIRED on PostgreSQL (inline signal source)
        }

        return Ok($"Inserted {person} and enqueued atomically (raw ADO; inline commit signal).");
    }

    // CAPABILITY 2 — EF Core coordinated transaction.
    // The DbContext helper runs inside EF's execution strategy and signals the commit via the EF interceptor (so no
    // manual SignalAsync is needed on this path, unlike raw enlistment above). SaveChanges and the enqueue commit
    // together.
    [Route("~/coordinated/ef")]
    public async Task<IActionResult> CoordinatedEntityFramework([FromServices] AppDbContext dbContext)
    {
        var person = new Person
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"ef-{Random.Shared.Next(1000, 9999)}"),
            Age = Random.Shared.Next(10, 99),
        };

        await dbContext.ExecuteCoordinatedTransactionAsync(
            async (ctx, ct) =>
            {
                ((AppDbContext)ctx).Persons.Add(person);
                await ctx.SaveChangesAsync(ct);

                await producer.EnqueueAsync(
                    new KafkaMessage(DateTime.UtcNow),
                    new EnqueueOptions { MessageName = _MessageName },
                    ct
                );
            },
            services,
            cancellationToken: HttpContext.RequestAborted
        );

        return Ok($"Inserted {person} and enqueued atomically (EF Core).");
    }

    // CAPABILITY 3 — rollback discards the enqueue (the core invariant).
    // The INSERT and the enqueue are buffered, then the operation throws. The transaction rolls back, so the row is
    // never persisted AND the message is never dispatched — no half-completed unit of work.
    [Route("~/coordinated/rollback")]
    public async Task<IActionResult> CoordinatedRollback([FromServices] AppDbContext dbContext)
    {
        var person = new Person
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"rollback-{Random.Shared.Next(1000, 9999)}"),
            Age = Random.Shared.Next(10, 99),
        };

        try
        {
            await dbContext.ExecuteCoordinatedTransactionAsync(
                async (ctx, ct) =>
                {
                    ((AppDbContext)ctx).Persons.Add(person);
                    await ctx.SaveChangesAsync(ct);

                    await producer.EnqueueAsync(
                        new KafkaMessage(DateTime.UtcNow),
                        new EnqueueOptions { MessageName = _MessageName },
                        ct
                    );

                    throw new InvalidOperationException("Simulated failure after the buffered enqueue.");
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

    // CAPABILITY 4 — delayed enqueue inside a coordinated transaction.
    // The delayed message is still bound to the commit: it is only scheduled if the transaction commits.
    [Route("~/coordinated/delay/{delaySeconds:int}")]
    public async Task<IActionResult> CoordinatedDelay(int delaySeconds, [FromServices] AppDbContext dbContext)
    {
        var person = new Person
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"delay-{Random.Shared.Next(1000, 9999)}"),
            Age = Random.Shared.Next(10, 99),
        };

        await dbContext.ExecuteCoordinatedTransactionAsync(
            async (ctx, ct) =>
            {
                ((AppDbContext)ctx).Persons.Add(person);
                await ctx.SaveChangesAsync(ct);

                await producer.EnqueueAsync(
                    new KafkaMessage(DateTime.UtcNow),
                    new EnqueueOptions { MessageName = _MessageName, Delay = TimeSpan.FromSeconds(delaySeconds) },
                    ct
                );
            },
            services,
            cancellationToken: HttpContext.RequestAborted
        );

        return Ok(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Inserted {person}; delayed enqueue ({delaySeconds}s) bound to the commit."
            )
        );
    }
}

public record KafkaMessage(DateTime Value);

public sealed class KafkaMessageConsumer : IConsume<KafkaMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<KafkaMessage> context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Subscriber output message: {context.Message.Value.ToString(CultureInfo.InvariantCulture)}");
        return ValueTask.CompletedTask;
    }
}
