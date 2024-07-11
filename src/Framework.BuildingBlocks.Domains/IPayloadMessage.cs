namespace Framework.BuildingBlocks.Domains;

public interface IPayloadMessage<out T> : IIntegrationMessage
{
    T Payload { get; }
}

public interface IMessagePayload
{
    static abstract string MessageKey { get; }
}

public class PayloadMessage<T> : Base<PayloadMessage<T>>, IPayloadMessage<T>
    where T : IMessagePayload
{
    public required string Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string MessageKey => T.MessageKey;

    public required T Payload { get; init; }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Id;
    }
}
