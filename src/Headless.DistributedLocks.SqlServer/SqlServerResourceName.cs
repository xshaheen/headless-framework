// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>Encodes logical resource names into SQL Server's <c>sp_getapplock @Resource</c> length budget.</summary>
public static class SqlServerResourceName
{
    private const string _HashPrefix = "sha256:";

    public static string Encode(string resource)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        if (resource.Length <= SqlServerDistributedLockFieldLimits.MaxResourceNameLength)
        {
            return resource;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resource));

        return _HashPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
