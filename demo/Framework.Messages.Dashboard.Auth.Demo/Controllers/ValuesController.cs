using Framework.Messages;
using Framework.Messages.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Controllers;

[Authorize]
[Route("api/[controller]")]
public sealed class ValuesController(IOutboxPublisher publisher, ILogger<ValuesController> logger) : Controller
{
    private const string _MyTopic = "sample.dashboard.auth";

    [Route("publish")]
    public async Task<IActionResult> Publish()
    {
        await publisher.PublishAsync(_MyTopic, new Person { Id = Random.Shared.Next(1, 100), Name = "Bar" });

        return Ok();
    }

    [NonAction]
    [CapSubscribe(_MyTopic)]
    public void Subscribe(Person p, [FromCap] MessageHeader header)
    {
        logger.LogInformation("Subscribe Invoked {Topic} {Person} {Headers}", _MyTopic, p, header);
    }

    public sealed record Person
    {
        public int Id { get; set; }
        public required string Name { get; set; }
    }
}
