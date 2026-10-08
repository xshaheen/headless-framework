// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Events;

public sealed class EntityEventDataTests
{
    private sealed record TestEntity(int Id, string Name);

    [Fact]
    public void should_keep_event_identity_out_of_business_payload_contracts()
    {
        typeof(EntityCreatedEventData<TestEntity>)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .Equal("Entity");
    }
}
