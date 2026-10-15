// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.Runtime;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Aws;

/// <summary>Tells an AWS call that could not reach SNS or SQS from one the service answered with a refusal.</summary>
internal static class AwsBrokerFailures
{
    /// <summary>
    /// Whether the call failed on the network or the service reported itself unavailable (a 5xx). A 4xx, such as a
    /// missing topic or a denied action, is a refusal of this message and returns <see langword="false" />.
    /// </summary>
    public static bool IsUnreachable(Exception exception) =>
        exception is AmazonServiceException { StatusCode: >= HttpStatusCode.InternalServerError }
        || BrokerFailures.IsConnectivity(exception);
}
