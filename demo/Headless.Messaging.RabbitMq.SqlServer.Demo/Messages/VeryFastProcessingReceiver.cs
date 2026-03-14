using Headless.Messaging;

namespace Demo.Messages;

public class VeryFastProcessingReceiver(ILogger<VeryFastProcessingReceiver> logger) : IConsume<TestMessage>
{
    public async ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Starting FAST processing handler {DateTime}: {MessageText}",
                DateTime.Now,
                context.Message.Text
            );
        }

        await Task.Delay(50, cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Ending   FAST processing handler {DateTime}: {MessageText}",
                DateTime.Now,
                context.Message.Text
            );
        }
    }
}
