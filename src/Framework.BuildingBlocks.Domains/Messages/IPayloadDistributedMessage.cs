// ReSharper disable once CheckNamespace

using Framework.BuildingBlocks.Domains.Helpers;

namespace Framework.BuildingBlocks.Domains;

public interface IDistributedMessagePayload
{
    static abstract string MessageKey { get; }
}

public interface IPayloadDistributedMessage<out T> : IDistributedMessage
{
    T Payload { get; }
}

public class PayloadDistributedMessage<T> : EquatableBase<PayloadDistributedMessage<T>>, IPayloadDistributedMessage<T>
    where T : IDistributedMessagePayload
{
    public string MessageKey => T.MessageKey;

    public required string Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required T Payload { get; init; }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Id;
    }
}
