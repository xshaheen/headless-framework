using Microsoft.AspNetCore.Mvc;

namespace Framework.Messages.Pulsar.InMemory.Demo.Controllers;

[Route("api/[controller]")]
public class ValuesController(IOutboxPublisher producer) : Controller, IConsumer
{
    [Route("~/without/transaction")]
    public async Task<IActionResult> WithoutTransaction()
    {
        await producer.PublishAsync("persistent://public/default/captesttopic", DateTime.Now);

        return Ok();
    }

    [CapSubscribe("persistent://public/default/captesttopic")]
    public void Test2T2(string value)
    {
        Console.WriteLine($"Subscriber output message: {value}");
    }
}
