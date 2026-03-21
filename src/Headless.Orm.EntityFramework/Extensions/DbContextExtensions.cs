// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for <see cref="DbContext"/>.
/// </summary>
internal static class DbContextExtensions
{
    public static T? GetServiceOrDefault<T>(this IInfrastructure<IServiceProvider> infrastructure)
        where T : class
    {
        try
        {
            return infrastructure.GetService<T>();
        }
        catch (InvalidOperationException)
        {
            return null; // GetService throw on service not found
        }
    }
}
