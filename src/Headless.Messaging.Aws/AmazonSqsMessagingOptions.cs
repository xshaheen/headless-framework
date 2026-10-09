// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Runtime;
using FluentValidation;

namespace Headless.Messaging.Aws;

/// <summary>
/// Configuration options for the Amazon SQS/SNS messaging transport.
/// </summary>
/// <remarks>
/// <see cref="Region"/> is required. When <see cref="Credentials"/> is <see langword="null"/>,
/// the AWS SDK resolves credentials through its standard chain (environment variables,
/// shared credentials file, IAM instance/task roles, etc.).
/// </remarks>
public sealed class AmazonSqsMessagingOptions
{
    /// <summary>The AWS region endpoint where SQS queues and SNS topics reside.</summary>
    public required RegionEndpoint Region { get; set; }

    /// <summary>
    /// Explicit AWS credentials to use instead of the SDK credential chain.
    /// Leave <see langword="null"/> to rely on the standard credential resolution order
    /// (environment variables, credentials file, IAM roles).
    /// </summary>
    public AWSCredentials? Credentials { get; set; }

    /// <summary>
    /// Overrides the SNS service URL derived from <see cref="Region"/>. Useful in local
    /// development environments such as LocalStack (for example <c>http://localhost:4566</c>).
    /// When <see langword="null"/>, the standard AWS regional endpoint is used.
    /// </summary>
    public string? SnsServiceUrl { get; set; }

    /// <summary>
    /// Overrides the SQS service URL derived from <see cref="Region"/>. Useful in local
    /// development environments such as LocalStack (for example <c>http://localhost:4566</c>).
    /// When <see langword="null"/>, the standard AWS regional endpoint is used.
    /// </summary>
    public string? SqsServiceUrl { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), SNS topics, SQS queues, the queue access policy, and the SNS-to-SQS
    /// subscriptions are created on first use. Set to <see langword="false"/> when they are managed externally (for
    /// example by Infrastructure as Code), so the runtime identity needs no create or policy permissions.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, the transport only looks entities up: queues by <c>sqs:GetQueueUrl</c> and topics by
    /// <c>sns:ListTopics</c>. Every topic, queue, and subscription must exist before the host starts, and each SNS-to-SQS
    /// subscription must enable raw message delivery, the envelope Bus consumers read.
    /// </remarks>
    public bool AutoProvision { get; set; } = true;

    /// <summary>
    /// How long a received message stays hidden from other consumers. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// Sent on every receive, so the queue's own default does not apply. Until the messaging core settles a message, the
    /// consumer extends its visibility by this amount every third of it, so a message waiting for a free handler or
    /// held by a slow one is not redelivered. A shorter value redelivers sooner after a process dies; a longer one costs
    /// fewer extension calls. Whole seconds from 1 second to 12 hours.
    /// </remarks>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long one receive waits for a message to arrive (SQS long polling). Defaults to 5 seconds.
    /// </summary>
    /// <remarks>
    /// Whole seconds from 0 to 20, the SQS limit. A longer wait costs fewer empty receives; 0 polls without waiting.
    /// </remarks>
    public TimeSpan ReceiveWaitTime { get; set; } = TimeSpan.FromSeconds(5);
}

internal sealed class AmazonSqsMessagingOptionsValidator : AbstractValidator<AmazonSqsMessagingOptions>
{
    public AmazonSqsMessagingOptionsValidator()
    {
        RuleFor(x => x.Region).NotNull();

        RuleFor(x => x.VisibilityTimeout)
            .InclusiveBetween(TimeSpan.FromSeconds(1), TimeSpan.FromHours(12))
            .Must(_IsWholeSeconds)
            .WithMessage("VisibilityTimeout must be whole seconds.");

        RuleFor(x => x.ReceiveWaitTime)
            .InclusiveBetween(TimeSpan.Zero, TimeSpan.FromSeconds(20))
            .Must(_IsWholeSeconds)
            .WithMessage("ReceiveWaitTime must be whole seconds.");
    }

    // SQS takes both values as whole seconds, so a fraction would be silently truncated.
    private static bool _IsWholeSeconds(TimeSpan value) => value.Ticks % TimeSpan.TicksPerSecond == 0;
}
