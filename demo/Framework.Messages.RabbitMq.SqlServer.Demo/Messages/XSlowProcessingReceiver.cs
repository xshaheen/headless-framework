using Framework.Messages;

namespace Demo.Messages;

public class XSlowProcessingReceiver(ILogger<XSlowProcessingReceiver> logger) : IConsume<TestMessage>
{
    public async ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"Starting SLOW processing handler {DateTime.Now:O}: {context.Message.Text}");
        await Task.Delay(10000, cancellationToken);
        logger.LogInformation($"Ending   SLOW processing handler {DateTime.Now:O}: {context.Message.Text}");
    }
}
