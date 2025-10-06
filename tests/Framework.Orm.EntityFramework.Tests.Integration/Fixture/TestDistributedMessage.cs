using Framework.Domains;

namespace Tests.Fixture;

public sealed record TestDistributedMessage(string Text) : IDistributedMessage
{
    public string UniqueId { get; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
