// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>Extension methods for <see cref="DbContext"/>.</summary>
[PublicAPI]
public static class DbContextExtensions
{
    /// <summary>
    /// Resolves <typeparamref name="T"/> from the EF Core internal service provider, returning
    /// <see langword="null"/> when the service is not registered instead of throwing.
    /// </summary>
    /// <typeparam name="T">The service type to resolve.</typeparam>
    /// <param name="infrastructure">An EF Core infrastructure accessor (typically a <c>DbContext</c>).</param>
    /// <returns>The resolved service, or <see langword="null"/> if not registered.</returns>
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
    /// <c>UseApplicationServiceProvider</c>), or <see langword="null"/> for a hand-constructed <c>DbContextOptions</c>
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
