// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;

namespace Tests.Entities;

/// <summary>
/// Basic entity without audit interfaces for simple ORM testing.
/// </summary>
public sealed class HarnessBasicEntity : IEntity<Guid>
{
    public Guid Id { get; private init; }

    public required string Name { get; init; }

    public IReadOnlyList<object> GetKeys() => [Id];
}
