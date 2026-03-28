using Headless.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[Authorize]
[Route("api/[controller]")]
public sealed class ValuesController(IOutboxPublisher publisher) : Controller
{
    private const string _MyTopic = "sample.dashboard.auth";

    [Route("publish")]
    public async Task<IActionResult> Publish()
    {
        await publisher.PublishAsync(
            new Person { Id = Random.Shared.Next(1, 100), Name = "Bar" },
            new PublishOptions { Topic = _MyTopic }
        );

        return Ok();
    }

    public sealed record Person
    {
        public int Id { get; set; }
        public required string Name { get; set; }
    }
}

public sealed class PersonConsumer(ILogger<PersonConsumer> logger) : IConsume<ValuesController.Person>
{
    public ValueTask Consume(ConsumeContext<ValuesController.Person> context, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Subscribe Invoked {Topic} {PersonId} {PersonName}",
                context.Topic,
                context.Message.Id,
                context.Message.Name
            );
        }
        return ValueTask.CompletedTask;
    }
}
