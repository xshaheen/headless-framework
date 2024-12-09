// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Tests.Exceptions;

public sealed class EntityNotFoundExceptionTests
{
    [Fact]
    public void should_set_properties_correctly_when_use_guid_key()
    {
        // given
        const string entity = "QueryableTestEntity";
        var key = Guid.NewGuid();

        // when
        var exception = new EntityNotFoundException(entity, key);

        // then
        exception.Entity.Should().Be(entity);
        exception.Key.Should().Be(key.ToString());
        exception.Message.Should().Be($"No entity founded - [{entity}:{key}]");
    }

    [Fact]
    public void should_set_properties_correctly_when_use_string_key()
    {
        // given
        const string entity = "QueryableTestEntity";
        const string key = "TestKey";

        // when
        var exception = new EntityNotFoundException(entity, key);

        // then
        exception.Entity.Should().Be(entity);
        exception.Key.Should().Be(key);
        exception.Message.Should().Be($"No entity founded - [{entity}:{key}]");
    }

    [Fact]
    public void should_set_properties_correctly_when_use_int_key()
    {
        // given
        const string entity = "QueryableTestEntity";
        const int key = 123;

        // when
        var exception = new EntityNotFoundException(entity, key);

        // then
        exception.Entity.Should().Be(entity);
        exception.Key.Should().Be(key.ToString(CultureInfo.InvariantCulture));
        exception.Message.Should().Be($"No entity founded - [{entity}:{key}]");
    }

    [Fact]
    public void should_set_properties_correctly_when_use_long_key()
    {
        // given
        const string entity = "QueryableTestEntity";
        const long key = 123L;

        // when
        var exception = new EntityNotFoundException(entity, key);

        // then
        exception.Entity.Should().Be(entity);
        exception.Key.Should().Be(key.ToString(CultureInfo.InvariantCulture));
        exception.Message.Should().Be($"No entity founded - [{entity}:{key}]");
    }
}
