// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IDistributedMessage
{
    string UniqueId { get; }

    string TypeKey { get; }

    DateTimeOffset Timestamp { get; }

    Dictionary<string, string> Properties { get; }
}

public interface IDistributedMessage<out T>
{
    T Payload { get; }
}
