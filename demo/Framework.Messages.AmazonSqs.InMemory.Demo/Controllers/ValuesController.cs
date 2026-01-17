using Framework.Messages;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxPublisher producer) : Controller, IConsumer
{
    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.PublishAsync("sample.aws.in-memory", DateTime.Now);

        return Ok();
    }

    [CapSubscribe("sample.aws.in-memory")]
    public void SubscribeInMemoryTopic(DateTime value)
    {
        Console.WriteLine($"Subscriber output message: {value}");
    }
}
