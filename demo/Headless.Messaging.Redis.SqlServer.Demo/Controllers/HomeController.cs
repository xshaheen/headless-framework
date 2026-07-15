using Headless.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class HomeController(IOutboxQueue publisher) : ControllerBase
{
    [HttpGet]
    public async Task Publish([FromQuery] string message = "test-message")
    {
        await publisher.EnqueueAsync(
            new Person { Age = 11, Name = "James" },
            new EnqueueOptions { MessageName = message }
        );
    }
}

public class Person
{
    public required string Name { get; set; }

    public int Age { get; set; }

    public override string ToString()
    {
        return "Name:" + Name + ", Age:" + Age;
    }
}

public sealed class PersonConsumer(ILogger<PersonConsumer> logger) : IConsume<Person>
{
    public ValueTask ConsumeAsync(ConsumeContext<Person> context, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "{MessageName} subscribed with value --> Name:{Name}, Age:{Age}",
                context.MessageName,
                context.Message.Name,
                context.Message.Age
            );
        }

        return ValueTask.CompletedTask;
    }
}
