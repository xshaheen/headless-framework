// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Fixture;

public sealed class LongKeyedEntity : IEntity<long>
{
    public long Id { get; private init; }

    public required string Name { get; init; }

    public IReadOnlyList<object> GetKeys()
    {
        return [Id];
    }
}
