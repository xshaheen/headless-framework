using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxPublisher producer) : Controller
{
    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.PublishAsync("sample.aws.in-memory", DateTime.Now);

        return Ok();
    }
}

public record AmazonSqsMessage(DateTime Value);

public sealed class AmazonSqsMessageConsumer : IConsume<AmazonSqsMessage>
{
    public ValueTask Consume(ConsumeContext<AmazonSqsMessage> context, CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $@"Subscriber output message: {context.Message.Value.ToString(CultureInfo.InvariantCulture)}"
        );

        return ValueTask.CompletedTask;
    }
}
