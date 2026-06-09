using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxBus producer) : Controller
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
            new PublishOptions { MessageName = "sample.rabbitmq.sqlserver" }
        );

        return Ok();
    }

    [Route("~/delay/{delaySeconds:int}")]
    public async Task<IActionResult> Delay(int delaySeconds)
    {
        await producer.PublishAsync(
            new Person { Id = 123, Name = "Bar" },
            new PublishOptions { MessageName = "sample.rabbitmq.sqlserver", Delay = TimeSpan.FromSeconds(delaySeconds) }
        );

        return Ok();
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
