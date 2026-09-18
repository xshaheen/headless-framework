using Dapper;
using Headless.Messaging;
using Headless.UnitOfWork;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using NameGenerator.Generators;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IBus producer, IUnitOfWorkManager unitOfWork) : Controller
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
            new PublishOptions { MessageName = _MessageName, DeliveryMode = DeliveryMode.Durable }
        );

        return Ok();
    }

    [Route("~/delay/{delaySeconds:int}")]
    public async Task<IActionResult> Delay(int delaySeconds)
    {
        await producer.PublishAsync(
            new Person { Name = "Bar", Age = 42 },
            new PublishOptions
            {
                MessageName = _MessageName,
                Delay = TimeSpan.FromSeconds(delaySeconds),
                DeliveryMode = DeliveryMode.Durable,
            }
        );

        return Ok();
    }

    // CAPABILITY 1 — raw ADO (Dapper) unit of work via IUnitOfWorkManager.RunAsync(connection, …).
    // RunAsync owns begin and commit: it opens the connection's transaction, runs the block, then commits. The
    // Publish enlists in that same transaction because it reads the ambient IUnitOfWorkManager.Current from this
    // scope, and Dapper needs the live transaction object, exposed as an IRelationalUnitOfWorkResource on uow.Resource.
    [Route("~/coordinated/adonet")]
    public async Task<IActionResult> CoordinatedAdoNet()
    {
        var person = new Person { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) };
        var ct = HttpContext.RequestAborted;

        await using var connection = new SqlConnection(AppDbContext.ConnectionString);
        await connection.OpenAsync(ct);

        await unitOfWork.RunAsync(
            connection,
            async (uow, token) =>
            {
                var resource = (IRelationalUnitOfWorkResource)uow.Resource!;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "INSERT INTO Persons(Name, Age, CreateTime) VALUES(@Name, @Age, GETDATE())",
                        new { person.Name, person.Age },
                        resource.Transaction,
                        cancellationToken: token
                    )
                );

                await producer.PublishAsync(
                    person,
                    new PublishOptions { MessageName = _MessageName, DeliveryMode = DeliveryMode.Durable },
                    token
                );
            },
            cancellationToken: ct
        );

        return Ok($"Inserted {person} and published atomically (raw ADO; unit-of-work-owned commit).");
    }

    // CAPABILITY 2 — EF Core unit of work via IUnitOfWorkManager.RunAsync(db, …).
    // RunAsync runs inside EF's execution strategy (retry-safe): begin (owned) → operation → CompleteAsync
    // (commits, then drains). SaveChanges and the publish commit together; a retried attempt discards its buffer
    // and re-runs cleanly.
    [Route("~/coordinated/ef")]
    public async Task<IActionResult> CoordinatedEntityFramework([FromServices] AppDbContext dbContext)
    {
        var person = new Person { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) };

        await unitOfWork.RunAsync(
            dbContext,
            async (_, ct) =>
            {
                dbContext.Persons.Add(person);
                await dbContext.SaveChangesAsync(ct);

                await producer.PublishAsync(
                    person,
                    new PublishOptions { MessageName = _MessageName, DeliveryMode = DeliveryMode.Durable },
                    ct
                );
            },
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
            await unitOfWork.RunAsync(
                dbContext,
                async (_, ct) =>
                {
                    dbContext.Persons.Add(person);
                    await dbContext.SaveChangesAsync(ct);

                    await producer.PublishAsync(
                        person,
                        new PublishOptions { MessageName = _MessageName, DeliveryMode = DeliveryMode.Durable },
                        ct
                    );

                    throw new InvalidOperationException("Simulated failure after the buffered publish.");
                },
                cancellationToken: HttpContext.RequestAborted
            );
        }
        catch (InvalidOperationException)
        {
            return Ok("Transaction rolled back: neither the row nor the message survived (discard-on-rollback).");
        }

        return Ok();
    }

    // CAPABILITY 4 — delayed publish inside a unit of work.
    // The delayed message is still bound to the commit: it is only scheduled if the transaction commits.
    [Route("~/coordinated/delay/{delaySeconds:int}")]
    public async Task<IActionResult> CoordinatedDelay(int delaySeconds, [FromServices] AppDbContext dbContext)
    {
        var person = new Person { Name = new RealNameGenerator().Generate(), Age = Random.Shared.Next(10, 99) };

        await unitOfWork.RunAsync(
            dbContext,
            async (_, ct) =>
            {
                dbContext.Persons.Add(person);
                await dbContext.SaveChangesAsync(ct);

                await producer.PublishAsync(
                    person,
                    new PublishOptions
                    {
                        MessageName = _MessageName,
                        Delay = TimeSpan.FromSeconds(delaySeconds),
                        DeliveryMode = DeliveryMode.Durable,
                    },
                    ct
                );
            },
            cancellationToken: HttpContext.RequestAborted
        );

        return Ok(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Inserted {person}; delayed publish ({delaySeconds}s) bound to the commit."
            )
        );
    }
}

public sealed class PersonConsumer : IConsume<Person>
{
    public ValueTask ConsumeAsync(ConsumeContext<Person> context, CancellationToken cancellationToken)
    {
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.UtcNow} Subscriber invoked, Info: {context.Message}"
            )
        );
        return ValueTask.CompletedTask;
    }
}
