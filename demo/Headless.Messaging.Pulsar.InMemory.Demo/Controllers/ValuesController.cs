using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxPublisher producer) : Controller
{
    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.PublishAsync(
            DateTime.Now,
            new PublishOptions { Topic = "persistent://public/default/headlesstesttopic" }
        );

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
