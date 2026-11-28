// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Aws;

namespace Tests;

/// <summary>SQS takes the visibility timeout and the receive wait as whole seconds within its own limits.</summary>
public sealed class AmazonSqsMessagingOptionsValidatorTests
{
    [Theory]
    [InlineData(0.0, 5.0)]
    [InlineData(43201.0, 5.0)]
    [InlineData(30.5, 5.0)]
    [InlineData(30.0, 21.0)]
    [InlineData(30.0, -1.0)]
    [InlineData(30.0, 2.5)]
    public void should_reject_values_sqs_cannot_take(double visibilitySeconds, double waitSeconds)
    {
        // given
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            VisibilityTimeout = TimeSpan.FromSeconds(visibilitySeconds),
            ReceiveWaitTime = TimeSpan.FromSeconds(waitSeconds),
        };

        // when
        var result = new AmazonSqsMessagingOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(43200.0, 20.0)]
    public void should_accept_the_sqs_limits(double visibilitySeconds, double waitSeconds)
    {
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            VisibilityTimeout = TimeSpan.FromSeconds(visibilitySeconds),
            ReceiveWaitTime = TimeSpan.FromSeconds(waitSeconds),
        };

        new AmazonSqsMessagingOptionsValidator().Validate(options).IsValid.Should().BeTrue();
    }
}
