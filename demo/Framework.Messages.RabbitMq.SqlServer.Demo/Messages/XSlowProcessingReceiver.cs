using Demo.TypedConsumers;

namespace Demo.Messages;

[QueueHandlerTopic("slowtopic")]
public class XSlowProcessingReceiver(ILogger<XSlowProcessingReceiver> logger) : QueueHandler
{
    public async Task Handle(TestMessage value)
    {
        logger.LogInformation($"Starting SLOW processing handler {DateTime.Now:O}: {value.Text}");
        await Task.Delay(10000);
        logger.LogInformation($"Ending   SLOW processing handler {DateTime.Now:O}: {value.Text}");
    }
}
