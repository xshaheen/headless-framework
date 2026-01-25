// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Runtime;
using Headless.Messaging.AwsSqs;

namespace Tests;

public sealed class AmazonSqsOptionsTests
{
    [Fact]
    public void should_require_region()
    {
        // given, when
        var options = new AmazonSqsOptions { Region = RegionEndpoint.USEast1 };

        // then
        options.Region.Should().Be(RegionEndpoint.USEast1);
    }

    [Fact]
    public void should_support_different_regions()
    {
        // given, when
        var usEast = new AmazonSqsOptions { Region = RegionEndpoint.USEast1 };
        var euWest = new AmazonSqsOptions { Region = RegionEndpoint.EUWest1 };
        var apSouth = new AmazonSqsOptions { Region = RegionEndpoint.APSouth1 };

        // then
        usEast.Region.SystemName.Should().Be("us-east-1");
        euWest.Region.SystemName.Should().Be("eu-west-1");
        apSouth.Region.SystemName.Should().Be("ap-south-1");
    }

    [Fact]
    public void should_support_localstack_endpoint()
    {
        // given
        const string localStackUrl = "http://localhost:4566";

        // when
        var options = new AmazonSqsOptions
        {
            Region = RegionEndpoint.USEast1,
            SqsServiceUrl = localStackUrl,
            SnsServiceUrl = localStackUrl,
        };

        // then
        options.SqsServiceUrl.Should().Be(localStackUrl);
        options.SnsServiceUrl.Should().Be(localStackUrl);
    }

    [Fact]
    public void should_allow_null_service_urls_for_production()
    {
        // given, when
        var options = new AmazonSqsOptions { Region = RegionEndpoint.USEast1 };

        // then
        options.SqsServiceUrl.Should().BeNull();
        options.SnsServiceUrl.Should().BeNull();
    }

    [Fact]
    public void should_allow_custom_credentials()
    {
        // given
        var credentials = new BasicAWSCredentials("access-key", "secret-key");

        // when
        var options = new AmazonSqsOptions { Region = RegionEndpoint.USEast1, Credentials = credentials };

        // then
        options.Credentials.Should().Be(credentials);
    }

    [Fact]
    public void should_allow_null_credentials_for_default_chain()
    {
        // given, when
        var options = new AmazonSqsOptions { Region = RegionEndpoint.USEast1 };

        // then - null credentials means use default credential chain (IAM role, env vars, etc.)
        options.Credentials.Should().BeNull();
    }

    [Fact]
    public void should_support_separate_sqs_and_sns_urls()
    {
        // given - some testing setups may have different endpoints for SQS and SNS
        var options = new AmazonSqsOptions
        {
            Region = RegionEndpoint.USEast1,
            SqsServiceUrl = "http://sqs-mock:4566",
            SnsServiceUrl = "http://sns-mock:4567",
        };

        // when, then
        options.SqsServiceUrl.Should().Be("http://sqs-mock:4566");
        options.SnsServiceUrl.Should().Be("http://sns-mock:4567");
    }

    [Fact]
    public void should_support_session_credentials()
    {
        // given
        var sessionCredentials = new SessionAWSCredentials("access-key", "secret-key", "session-token");

        // when
        var options = new AmazonSqsOptions { Region = RegionEndpoint.USEast1, Credentials = sessionCredentials };

        // then
        options.Credentials.Should().BeOfType<SessionAWSCredentials>();
    }
}
