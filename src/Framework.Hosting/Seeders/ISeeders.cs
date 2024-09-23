// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.DependencyInjection;

namespace Framework.Hosting.Seeders;

public interface ISeeder
{
    public ValueTask SeedAsync();
}

public static class DbSeedersExtensions
{
    public static void AddDataSeeder<T>(this IServiceCollection services)
        where T : class, ISeeder
    {
        services.AddTransient<ISeeder, T>().AddTransient<T>();
    }

    public static async Task SeedAsync(this IServiceProvider services)
    {
        var typeOfSeeders = await _GetSeedersAsync(services);

        foreach (var type in typeOfSeeders)
        {
            await using var scope = services.CreateAsyncScope();
            var seeder = (ISeeder)scope.ServiceProvider.GetRequiredService(type);

            await seeder.SeedAsync();
        }
    }

    private static async Task<IEnumerable<Type>> _GetSeedersAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var typeOfSeeders = scope.ServiceProvider.GetServices<ISeeder>().Select(x => x.GetType());

        return typeOfSeeders;
    }
}
