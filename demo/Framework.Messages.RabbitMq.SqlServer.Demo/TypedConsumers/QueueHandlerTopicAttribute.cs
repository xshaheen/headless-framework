namespace Demo.TypedConsumers;

[AttributeUsage(AttributeTargets.Class)]
public class QueueHandlerTopicAttribute(string topic) : Attribute
{
    public string Topic { get; } = topic;
}
