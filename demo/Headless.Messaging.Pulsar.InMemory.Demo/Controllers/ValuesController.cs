using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxPublisher producer) : Controller
{
    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.PublishAsync("persistent://public/default/headlesstesttopic", DateTime.Now);

        return Ok();
    }
}

public record PulsarMessage(string Value);

public sealed class PulsarMessageConsumer : IConsume<PulsarMessage>
{
    public ValueTask Consume(ConsumeContext<PulsarMessage> context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Subscriber output message: {context.Message.Value}");
        return ValueTask.CompletedTask;
    }
}
