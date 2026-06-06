// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Extension methods for <see cref="DbContext"/>.</summary>
[PublicAPI]
public static class DbContextExtensions
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

    /// <summary>
    /// The application service provider EF Core was built with (set by the Headless registration via
    /// <c>UseApplicationServiceProvider</c>), or <c>null</c> for a hand-constructed <c>DbContextOptions</c>
    /// outside the DI pipeline — in which case the generators fall back to their framework default.
    /// </summary>
    internal static IServiceProvider? GetApplicationServices(this DbContext context)
    {
        return context
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider;
    }
}
