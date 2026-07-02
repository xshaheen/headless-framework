// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Aws;

internal static class AwsBrokerEndpoint
{
    public static string Resolve(string? serviceUrl, string serviceName, AmazonSqsOptions options)
    {
        if (
            Uri.TryCreate(serviceUrl, UriKind.Absolute, out var serviceUri)
            && !string.IsNullOrWhiteSpace(serviceUri.Host)
        )
        {
            return serviceUri.IsDefaultPort
                ? serviceUri.Host
                : string.Create(CultureInfo.InvariantCulture, $"{serviceUri.Host}:{serviceUri.Port}");
        }

        return $"{serviceName}.{options.Region.SystemName}.{options.Region.PartitionDnsSuffix}";
    }
}
