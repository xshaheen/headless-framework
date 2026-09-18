// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Builds the feature-owned storage options these tests hand to a directly constructed storage or
/// initializer, standing in for the DI registration the setup builder would normally make.
/// </summary>
internal static class TestStorageOptions
{
    public static IOptions<MessagingStorageOptions> For(string schema = MessagingStorageOptions.DefaultSchema)
    {
        return Options.Create(new MessagingStorageOptions { Schema = schema });
    }
}
