// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface IMessageSubscribeMedium<out TPayload>
{
    string UniqueId { get; }

    string TypeKey { get; }

    string CorrelationId { get; }

    DateTimeOffset Timestamp { get; }

    Dictionary<string, string> Properties { get; }

    TPayload Payload { get; }
}
