// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

[PublicAPI]
public class DistributedMessage<T> : EquatableBase<DistributedMessage<T>>, IDistributedMessage<T>
    where T : IDistributedMessagePayload
{
    public string TypeKey => T.TypeKey;

    public required string UniqueId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public required T Payload { get; init; }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return UniqueId;
    }
}

public interface IDistributedMessagePayload
{
    static abstract string TypeKey { get; }
}
