// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Tests;

/// <summary>A shared-server fixture on another image reference: the default image's tag without its digest.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class RabbitMqCustomImageFixture : HeadlessRabbitMqFixture, ICollectionFixture<RabbitMqCustomImageFixture>
{
    public const string CustomImage = "rabbitmq:3-alpine";

    protected override string Image => CustomImage;
}

[Collection<RabbitMqCustomImageFixture>]
public sealed class RabbitMqFixtureImageTests(RabbitMqCustomImageFixture fixture) : TestBase
{
    [Fact]
    public async Task should_run_the_image_a_subclass_names()
    {
        fixture.Container.Image.FullName.Should().StartWith(RabbitMqCustomImageFixture.CustomImage);

        var factory = new ConnectionFactory { Uri = new Uri(fixture.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(AbortToken);

        connection.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void should_share_a_broker_of_its_own_image_apart_from_the_default_one()
    {
        if (!SharedTestContainers.IsEnabled)
        {
            return;
        }

        fixture
            .Container.Name.TrimStart('/')
            .Should()
            .Be(
                SharedTestContainers.ContainerName(
                    "rabbitmq",
                    RabbitMqCustomImageFixture.CustomImage,
                    RabbitMqBuilder.DefaultUsername
                )
            )
            .And.NotBe(
                SharedTestContainers.ContainerName("rabbitmq", TestImages.RabbitMq, RabbitMqBuilder.DefaultUsername)
            );
    }
}

/// <summary>A private-container fixture on another image reference: the default image's tag without its digest.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class RedisCustomImageFixture : HeadlessRedisFixture, ICollectionFixture<RedisCustomImageFixture>
{
    public const string CustomImage = "redis:7-alpine";

    protected override string Image => CustomImage;
}

[Collection<RedisCustomImageFixture>]
public sealed class RedisFixtureImageTests(RedisCustomImageFixture fixture) : TestBase
{
    [Fact]
    public async Task should_run_the_image_a_subclass_names()
    {
        fixture.Container.Image.FullName.Should().StartWith(RedisCustomImageFixture.CustomImage);

        var result = await fixture.Container.ExecAsync(["redis-cli", "PING"], AbortToken);

        result.Stdout.Trim().Should().Be("PONG");
    }
}
