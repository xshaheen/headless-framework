using Framework.Domain;

namespace Tests.Fixture;

public sealed class BasicEntity : IEntity<Guid>
{
    public Guid Id { get; private init; }

    public required string Name { get; init; }

    public IReadOnlyList<object> GetKeys() => [Id];
}
