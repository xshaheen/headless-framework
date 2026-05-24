using Headless.Messaging;

namespace Demo.Messages;

public class XSlowProcessingReceiver(ILogger<XSlowProcessingReceiver> logger) : IConsume<TestMessage>
{
    public async ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Starting SLOW processing handler {DateTime}: {MessageText}",
                DateTime.UtcNow,
                context.Message.Text
            );
        }

        await Task.Delay(10000, cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Ending   SLOW processing handler {DateTime}: {MessageText}",
                DateTime.UtcNow,
                context.Message.Text
            );
        }
    }
}
