using Framework.Messages;

namespace Demo;

public class SampleSubscriber : IConsumer
{
    public record Message(string Content);

    [CapSubscribe("cap.sample.tests")]
    public void Handle(Message message)
    {
        Console.WriteLine($"Message {message.Content} received");
    }
}
