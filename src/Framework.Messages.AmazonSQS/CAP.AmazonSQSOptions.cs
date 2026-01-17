// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Runtime;

namespace Framework.Messages;

// ReSharper disable once InconsistentNaming
public class AmazonSQSOptions
{
    public RegionEndpoint Region { get; set; } = default!;

    public AWSCredentials? Credentials { get; set; }

    /// <summary>
    /// Overrides Service Url deduced from AWS Region. To use in local development environments like localstack.
    /// </summary>
    public string? SnsServiceUrl { get; set; }

    /// <summary>
    /// Overrides Service Url deduced from AWS Region. To use in local development environments like localstack.
    /// </summary>
    public string? SqsServiceUrl { get; set; }
}
