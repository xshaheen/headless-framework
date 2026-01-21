using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class HomeController(IOutboxPublisher publisher) : ControllerBase
{
    [HttpGet]
    public async Task Publish([FromQuery] string message = "test-message")
    {
        await publisher.PublishAsync(message, new Person { Age = 11, Name = "James" });
    }
}

public class Person
{
    public required string Name { get; set; }

    public int Age { get; set; }

    public override string ToString() => "Name:" + Name + ", Age:" + Age;
}

public sealed class PersonConsumer(ILogger<PersonConsumer> logger) : IConsume<Person>
{
    public ValueTask Consume(ConsumeContext<Person> context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "{ContextTopic} subscribed with value --> {ContextMessage}",
            context.Topic,
            context.Message
        );

        return ValueTask.CompletedTask;
    }
}
