// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Framework.Hosting.Seeders;

[PublicAPI]
public static class DbSeedersExtensions
{
    public static void AddSeeder<T>(this IServiceCollection services)
        where T : class, ISeeder
    {
        services.AddTransient<ISeeder, T>().AddTransient<T>();
    }

    public static async Task SeedAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var seeders = scope.ServiceProvider.GetServices<ISeeder>();

        foreach (var seeder in seeders)
        {
            await seeder.SeedAsync();
        }
    }
}
