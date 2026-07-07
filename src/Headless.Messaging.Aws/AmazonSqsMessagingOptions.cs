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
}

internal sealed class AmazonSqsMessagingOptionsValidator : AbstractValidator<AmazonSqsMessagingOptions>
{
    public AmazonSqsMessagingOptionsValidator()
    {
        RuleFor(x => x.Region).NotNull();
    }
}
