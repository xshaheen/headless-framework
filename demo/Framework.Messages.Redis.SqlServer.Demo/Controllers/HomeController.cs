using Framework.Messages;
using Framework.Messages.Messages;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class HomeController(ILogger<HomeController> logger, IOutboxPublisher publisher) : ControllerBase
{
    [HttpGet]
    public async Task Publish([FromQuery] string message = "test-message")
    {
        await publisher.PublishAsync(message, new Person { Age = 11, Name = "James" });
    }

    [CapSubscribe("test-message")]
    [CapSubscribe("test-message-1")]
    [CapSubscribe("test-message-2")]
    [CapSubscribe("test-message-3")]
    [NonAction]
    public void Subscribe(Person p, [FromCap] MessageHeader header)
    {
        logger.LogInformation($"{header[Headers.MessageName]} subscribed with value --> " + p);
    }
}

public class Person
{
    public required string Name { get; set; }

    public int Age { get; set; }

    public override string ToString() => "Name:" + Name + ", Age:" + Age;
}
