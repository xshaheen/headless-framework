using Framework.Messages;

namespace Demo.Messages;

public class VeryFastProcessingReceiver(ILogger<VeryFastProcessingReceiver> logger) : IConsume<TestMessage>
{
    public async ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"Starting FAST processing handler {DateTime.Now:O}: {context.Message.Text}");
        await Task.Delay(50, cancellationToken);
        logger.LogInformation($"Ending   FAST processing handler {DateTime.Now:O}: {context.Message.Text}");
    }
}
