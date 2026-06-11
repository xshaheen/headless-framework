using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxQueue producer) : Controller
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
