using Framework.Domains;

namespace Tests.Fixture;

public sealed record TestDistributedMessage(string Text) : IDistributedMessage
{
    public Guid UniqueId { get; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
