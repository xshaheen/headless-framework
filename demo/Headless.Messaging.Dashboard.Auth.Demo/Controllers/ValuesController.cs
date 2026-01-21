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
        await publisher.PublishAsync(_MyTopic, new Person { Id = Random.Shared.Next(1, 100), Name = "Bar" });

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
        logger.LogInformation("Subscribe Invoked {Topic} {Person}", context.Topic, context.Message);
        return ValueTask.CompletedTask;
    }
}
