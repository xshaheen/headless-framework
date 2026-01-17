using Demo.TypedConsumers;

namespace Demo.Messages;

[QueueHandlerTopic("fasttopic")]
public class VeryFastProcessingReceiver(ILogger<VeryFastProcessingReceiver> logger) : QueueHandler
{
    public async Task Handle(TestMessage value)
    {
        logger.LogInformation($"Starting FAST processing handler {DateTime.Now:O}: {value.Text}");
        await Task.Delay(50);
        logger.LogInformation($"Ending   FAST processing handler {DateTime.Now:O}: {value.Text}");
    }
}
