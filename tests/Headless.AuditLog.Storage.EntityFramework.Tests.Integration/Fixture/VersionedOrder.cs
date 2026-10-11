using Headless.Domain;

namespace Tests.Fixture;

/// <summary>
/// Audited entity carrying the framework's concurrency stamp, so tests can observe the tracked
/// values and the token state a consumer sees after an audited save in the same scope.
/// </summary>
public sealed class VersionedOrder : IEntity<Guid>, IHasConcurrencyStamp
{
    public Guid Id { get; set; }

    public string CustomerName { get; set; } = "";

    // Store-computed mirror of CustomerName; read back through RETURNING on insert and update.
    public string? NameUpper { get; private init; }

    public string? ConcurrencyStamp { get; private init; }

    public IReadOnlyList<object> GetKeys()
    {
        return [Id];
    }
}
