// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

[PublicAPI]
public sealed class PublishMessageOptions
{
    public required string UniqueId { get; set; }

    public required string CorrelationId { get; set; }

    public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
}
