// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessagingCorrelationScopeTests : TestBase
{
    [Fact]
    public void should_set_current_when_scope_begun()
    {
        // given
        var correlationId = Faker.Random.Guid().ToString();

        // when
        using var scope = MessagingCorrelationScope.Begin(correlationId);

        // then
        MessagingCorrelationScope.Current.Should().NotBeNull();
        MessagingCorrelationScope.Current!.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public void should_clear_current_when_disposed()
    {
        // given
        var correlationId = Faker.Random.Guid().ToString();
        var scope = MessagingCorrelationScope.Begin(correlationId);

        // when
        scope.Dispose();

        // then
        MessagingCorrelationScope.Current.Should().BeNull();
    }

    [Fact]
    public void should_restore_parent_scope_when_nested_scope_disposed()
    {
        // given
        var parentId = Faker.Random.Guid().ToString();
        var childId = Faker.Random.Guid().ToString();

        using var parentScope = MessagingCorrelationScope.Begin(parentId);
        var childScope = MessagingCorrelationScope.Begin(childId);

        // when
        childScope.Dispose();

        // then
        MessagingCorrelationScope.Current.Should().NotBeNull();
        MessagingCorrelationScope.Current!.CorrelationId.Should().Be(parentId);
    }

    [Fact]
    public void should_increment_sequence_atomically()
    {
        // given
        var correlationId = Faker.Random.Guid().ToString();
        const int initialSequence = 5;
        using var scope = MessagingCorrelationScope.Begin(correlationId, initialSequence);

        // when
        var seq1 = scope.IncrementSequence();
        var seq2 = scope.IncrementSequence();
        var seq3 = scope.IncrementSequence();

        // then
        seq1.Should().Be(6);
        seq2.Should().Be(7);
        seq3.Should().Be(8);
    }

    [Fact]
    public void should_have_null_current_when_no_scope_active()
    {
        // given - no scope

        // when
        var current = MessagingCorrelationScope.Current;

        // then
        current.Should().BeNull();
    }
}
