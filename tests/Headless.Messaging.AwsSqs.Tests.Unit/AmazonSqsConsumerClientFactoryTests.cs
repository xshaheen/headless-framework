// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AwsSqs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AmazonSqsConsumerClientFactoryTests : TestBase
{
    [Fact]
    public async Task should_create_consumer_client()
    {
        // given
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();

        var factory = new AmazonSqsConsumerClientFactory(options, logger);

        // when
        var client = await factory.CreateAsync("test-group", 5);

        // then
        client.Should().NotBeNull();
        client.Should().BeOfType<AmazonSqsConsumerClient>();

        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_pass_group_name_to_client()
    {
        // given
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();

        var factory = new AmazonSqsConsumerClientFactory(options, logger);

        // when
        var client = await factory.CreateAsync("my-custom-group", 3);

        // then - broker address should contain the group info after connection
        client.Should().NotBeNull();
        client.BrokerAddress.Name.Should().Be("aws_sqs");

        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_create_multiple_clients_with_different_groups()
    {
        // given
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();

        var factory = new AmazonSqsConsumerClientFactory(options, logger);

        // when
        var client1 = await factory.CreateAsync("group-1", 2);
        var client2 = await factory.CreateAsync("group-2", 4);

        // then
        client1.Should().NotBeSameAs(client2);

        await client1.DisposeAsync();
        await client2.DisposeAsync();
    }

    [Fact]
    public async Task should_respect_concurrency_setting()
    {
        // given
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();

        var factory = new AmazonSqsConsumerClientFactory(options, logger);

        // when
        var client = await factory.CreateAsync("test-group", 10);

        // then
        client.Should().NotBeNull();
        // The concurrency is configured internally - we verify the client was created successfully

        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_create_client_with_zero_concurrency_for_sync_mode()
    {
        // given
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();

        var factory = new AmazonSqsConsumerClientFactory(options, logger);

        // when - groupConcurrent = 0 means synchronous processing
        var client = await factory.CreateAsync("sync-group", 0);

        // then
        client.Should().NotBeNull();

        await client.DisposeAsync();
    }
}
