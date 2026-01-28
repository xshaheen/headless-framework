// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Events;

public sealed class EntityEventDataTests
{
    private sealed record TestEntity(int Id, string Name);

    [Fact]
    public void should_create_entity_created_event_data()
    {
        var entity = new TestEntity(1, "Test");

        var eventData = new EntityCreatedEventData<TestEntity>(entity);

        eventData.Should().NotBeNull();
        eventData.Entity.Should().BeSameAs(entity);
    }

    [Fact]
    public void should_create_entity_updated_event_data()
    {
        var entity = new TestEntity(1, "Test");

        var eventData = new EntityUpdatedEventData<TestEntity>(entity);

        eventData.Should().NotBeNull();
        eventData.Entity.Should().BeSameAs(entity);
    }

    [Fact]
    public void should_create_entity_deleted_event_data()
    {
        var entity = new TestEntity(1, "Test");

        var eventData = new EntityDeletedEventData<TestEntity>(entity);

        eventData.Should().NotBeNull();
        eventData.Entity.Should().BeSameAs(entity);
    }

    [Fact]
    public void should_create_entity_changed_event_data()
    {
        var entity = new TestEntity(1, "Test");

        var eventData = new EntityChangedEventData<TestEntity>(entity);

        eventData.Should().NotBeNull();
        eventData.Entity.Should().BeSameAs(entity);
    }

    [Fact]
    public void should_implement_i_local_message()
    {
        var entity = new TestEntity(1, "Test");

        var createdEvent = new EntityCreatedEventData<TestEntity>(entity);
        var updatedEvent = new EntityUpdatedEventData<TestEntity>(entity);
        var deletedEvent = new EntityDeletedEventData<TestEntity>(entity);
        var changedEvent = new EntityChangedEventData<TestEntity>(entity);

        createdEvent.Should().BeAssignableTo<ILocalMessage>();
        updatedEvent.Should().BeAssignableTo<ILocalMessage>();
        deletedEvent.Should().BeAssignableTo<ILocalMessage>();
        changedEvent.Should().BeAssignableTo<ILocalMessage>();
    }

    [Fact]
    public void should_preserve_entity_reference()
    {
        var entity = new TestEntity(42, "Original");

        var createdEvent = new EntityCreatedEventData<TestEntity>(entity);
        var updatedEvent = new EntityUpdatedEventData<TestEntity>(entity);
        var deletedEvent = new EntityDeletedEventData<TestEntity>(entity);
        var changedEvent = new EntityChangedEventData<TestEntity>(entity);

        createdEvent.Entity.Id.Should().Be(42);
        updatedEvent.Entity.Name.Should().Be("Original");
        deletedEvent.Entity.Should().Be(entity);
        changedEvent.Entity.Should().Be(entity);
    }
}
