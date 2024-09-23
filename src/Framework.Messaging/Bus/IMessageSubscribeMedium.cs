#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface IMessageSubscribeMedium<out TPayload>
{
    string UniqueId { get; }

    string TypeKey { get; }

    string CorrelationId { get; }

    DateTimeOffset Timestamp { get; }

    IDictionary<string, string> Properties { get; }

    TPayload Payload { get; }
}
