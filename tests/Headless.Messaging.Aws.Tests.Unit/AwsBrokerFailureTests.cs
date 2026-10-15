// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.Runtime;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;

namespace Tests;

public sealed class AwsBrokerFailureTests : TestBase
{
    [Fact]
    public void should_count_a_network_failure_or_an_unavailable_service()
    {
        AwsBrokerFailures
            .IsUnreachable(new AmazonServiceException("down", new HttpRequestException("refused")))
            .Should()
            .BeTrue();
        AwsBrokerFailures
            .IsUnreachable(new AmazonServiceException("unavailable") { StatusCode = HttpStatusCode.ServiceUnavailable })
            .Should()
            .BeTrue();
    }

    [Fact]
    public void should_not_count_a_refusal_such_as_a_missing_topic()
    {
        AwsBrokerFailures
            .IsUnreachable(new AmazonServiceException("not found") { StatusCode = HttpStatusCode.NotFound })
            .Should()
            .BeFalse();
    }
}
