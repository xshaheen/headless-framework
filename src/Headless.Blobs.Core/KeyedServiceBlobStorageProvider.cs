// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Blobs;

/// <summary>
/// <see cref="IBlobStorageProvider"/> over the container's keyed <see cref="IBlobStorage"/> registrations —
/// resolves named instances added through the <c>AddNamed</c> setup overloads.
/// </summary>
internal sealed class KeyedServiceBlobStorageProvider(IServiceProvider serviceProvider) : IBlobStorageProvider
{
    public IBlobStorage GetStorage(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<IBlobStorage>(name)
            ?? throw new InvalidOperationException(
                $"No blob storage is registered under the name '{name}'. Register a named instance first — for "
                    + $"example setup.AddNamed(\"{name}\", i => i.UseFileSystem(…)), i.UseAws(…), i.UseAzure(…), "
                    + "i.UseCloudflareR2(…), i.UseRedis(…), or i.UseSsh(…)."
            );
    }

    public IBlobStorage? GetStorageOrNull(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<IBlobStorage>(name);
    }
}
