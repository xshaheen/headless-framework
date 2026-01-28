using Headless.Domain;

namespace Tests.Fixture;

public sealed record TestDistributedMessage(string Text) : IDistributedMessage
{
    public string UniqueId { get; } = Guid.NewGuid().ToString();

    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
