// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MediumMessageTests : TestBase
{
    [Fact]
    public void should_create_medium_message_with_required_properties()
    {
        // given
        var dbId = Faker.Random.Guid().ToString();
        var message = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Faker.Random.Guid().ToString(),
            },
            new { Value = "test" }
        );
        const string content = """{"Value":"test"}""";

        // when
        var mediumMessage = new MediumMessage
        {
            DbId = dbId,
            Origin = message,
            Content = content,
        };

        // then
        mediumMessage.DbId.Should().Be(dbId);
        mediumMessage.Origin.Should().BeSameAs(message);
        mediumMessage.Content.Should().Be(content);
    }

    [Fact]
    public void should_default_added_to_default_datetime()
    {
        // when
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
        };

        // then
        mediumMessage.Added.Should().Be(default);
    }

    [Fact]
    public void should_set_added_datetime()
    {
        // given
        var addedTime = new DateTime(2026, 1, 25, 12, 0, 0, DateTimeKind.Utc);

        // when
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
            Added = addedTime,
        };

        // then
        mediumMessage.Added.Should().Be(addedTime);
    }

    [Fact]
    public void should_default_expires_at_to_null()
    {
        // when
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
        };

        // then
        mediumMessage.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void should_set_expires_at()
    {
        // given
        var expiresAt = new DateTime(2026, 1, 26, 12, 0, 0, DateTimeKind.Utc);

        // when
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
            ExpiresAt = expiresAt,
        };

        // then
        mediumMessage.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public void should_default_retries_to_zero()
    {
        // when
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
        };

        // then
        mediumMessage.Retries.Should().Be(0);
    }

    [Fact]
    public void should_set_retries()
    {
        // when
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
            Retries = 5,
        };

        // then
        mediumMessage.Retries.Should().Be(5);
    }

    [Fact]
    public void should_allow_modification_of_retries()
    {
        // given
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
            Retries = 0,
        };

        // when
        mediumMessage.Retries = 3;

        // then
        mediumMessage.Retries.Should().Be(3);
    }

    [Fact]
    public void should_allow_modification_of_added()
    {
        // given
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
        };
        var newAddedTime = DateTime.UtcNow;

        // when
        mediumMessage.Added = newAddedTime;

        // then
        mediumMessage.Added.Should().Be(newAddedTime);
    }

    [Fact]
    public void should_allow_modification_of_expires_at()
    {
        // given
        var mediumMessage = new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = new Message(),
            Content = "{}",
        };
        var expiresAt = DateTime.UtcNow.AddHours(1);

        // when
        mediumMessage.ExpiresAt = expiresAt;

        // then
        mediumMessage.ExpiresAt.Should().Be(expiresAt);
    }
}
