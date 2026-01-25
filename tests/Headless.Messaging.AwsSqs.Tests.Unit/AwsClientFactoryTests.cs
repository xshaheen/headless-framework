// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Headless.Messaging.AwsSqs;

namespace Tests;

public sealed class AwsClientFactoryTests
{
    [Fact]
    public void should_create_sns_client_with_region()
    {
        // given
        var options = new AmazonSqsOptions { Region = RegionEndpoint.USWest2 };

        // when
        using var client = AwsClientFactory.CreateSnsClient(options);

        // then
        client.Should().NotBeNull();
        client.Should().BeOfType<AmazonSimpleNotificationServiceClient>();
    }

    [Fact]
    public void should_create_sns_client_with_credentials()
    {
        // given
        var credentials = new BasicAWSCredentials("access-key", "secret-key");
        var options = new AmazonSqsOptions { Region = RegionEndpoint.USWest2, Credentials = credentials };

        // when
        using var client = AwsClientFactory.CreateSnsClient(options);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public void should_create_sns_client_with_service_url()
    {
        // given
        var options = new AmazonSqsOptions
        {
            Region = RegionEndpoint.USEast1,
            SnsServiceUrl = "http://localhost:4566",
        };

        // when
        using var client = AwsClientFactory.CreateSnsClient(options);

        // then
        client.Should().NotBeNull();
        // ServiceURL is configured via AmazonSimpleNotificationServiceConfig
    }

    [Fact]
    public void should_create_sns_client_with_service_url_and_credentials()
    {
        // given
        var credentials = new BasicAWSCredentials("access-key", "secret-key");
        var options = new AmazonSqsOptions
        {
            Region = RegionEndpoint.USEast1,
            SnsServiceUrl = "http://localhost:4566",
            Credentials = credentials,
        };

        // when
        using var client = AwsClientFactory.CreateSnsClient(options);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public void should_create_sqs_client_with_region()
    {
        // given
        var options = new AmazonSqsOptions { Region = RegionEndpoint.EUWest1 };

        // when
        using var client = AwsClientFactory.CreateSqsClient(options);

        // then
        client.Should().NotBeNull();
        client.Should().BeOfType<AmazonSQSClient>();
    }

    [Fact]
    public void should_create_sqs_client_with_credentials()
    {
        // given
        var credentials = new BasicAWSCredentials("access-key", "secret-key");
        var options = new AmazonSqsOptions { Region = RegionEndpoint.EUWest1, Credentials = credentials };

        // when
        using var client = AwsClientFactory.CreateSqsClient(options);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public void should_create_sqs_client_with_service_url()
    {
        // given
        var options = new AmazonSqsOptions
        {
            Region = RegionEndpoint.USEast1,
            SqsServiceUrl = "http://localhost:4566",
        };

        // when
        using var client = AwsClientFactory.CreateSqsClient(options);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public void should_create_sqs_client_with_service_url_and_credentials()
    {
        // given
        var credentials = new BasicAWSCredentials("access-key", "secret-key");
        var options = new AmazonSqsOptions
        {
            Region = RegionEndpoint.USEast1,
            SqsServiceUrl = "http://localhost:4566",
            Credentials = credentials,
        };

        // when
        using var client = AwsClientFactory.CreateSqsClient(options);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public void should_ignore_whitespace_service_url()
    {
        // given - whitespace service URL should be treated as null (use region)
        var options = new AmazonSqsOptions { Region = RegionEndpoint.USEast1, SqsServiceUrl = "   ", SnsServiceUrl = "   " };

        // when
        using var sqsClient = AwsClientFactory.CreateSqsClient(options);
        using var snsClient = AwsClientFactory.CreateSnsClient(options);

        // then
        sqsClient.Should().NotBeNull();
        snsClient.Should().NotBeNull();
    }
}
